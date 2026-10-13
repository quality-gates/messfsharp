namespace MessFSharp

open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Domain

/// Finds data that enters a function or member other than through its arguments,
/// or leaves it other than through its return value. Syntax only: no type information.
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyMethods")>]
module Flows =
    type private Owner =
        { Name: string
          Line: int
          Key: int * int
          TypeKey: (int * int) option
          Self: string option
          Path: string list }

    type private CallArgument =
        | ArgumentPath of string list
        | ArgumentCallable of Owner
        | ArgumentOther

    type private Access =
        | Read of string list
        | Write of string list * readsExisting: bool
        | MutatingCall of string list
        | Call of string list * CallArgument list
        | Ambient of FlowDirection * string * string list

    type private StorageExpr =
        | DirectStorage of string list
        | ShallowCopyStorage of string list
        | CallResultStorage of string list * string list list
        | FreshStorage
        | JoinedStorage of StorageExpr list

    type private Fact =
        | Callable of Owner
        | Parameter of (int * int) * string * bool * int
        | Bound of (int * int) * string * range
        | LocalName of (int * int) * string * pos: (int * int)
        | CallableStorage of (int * int) * string * string list * range
        | Alias of (int * int) * string * string list * range
        | Storage of (int * int) * string * StorageExpr * range
        | ReturnAlias of (int * int) * string
        | ReturnCallable of (int * int) * (int * int)
        | TypeData of (int * int) * string * bool
        | TypeProperty of (int * int) * string * bool
        | TypeMethod of (int * int) * string
        | PureConstructor of path: string list * name: string
        | ModuleData of path: string list * name: string * tracked: bool * container: bool * pos: (int * int)
        | Open of path: string list * target: string list * pos: (int * int)
        | Candidate of Owner * Access * range

    type private Resolution =
        | Argument of string * bool
        | Local
        | Data of FlowSource * string * bool
        | ModuleValue of string
        | CallableTarget of Owner
        | Unknown of string

    type private StorageOrigin =
        | ParameterOrigin of string
        | SharedOrigin of FlowSource * string
        | FreshOrigin
        | UnknownOrigin of string

    type private StorageSummary =
        { Outer: Set<StorageOrigin>
          Reachable: Set<StorageOrigin>
          Complete: bool }

    type private UnknownReason = UnknownCall of string

    type private SummaryEffect =
        | KnownEffect of FlowDirection * FlowSource * string
        | ParameterWrite of string
        | ParameterCall of string * CallArgument list
        | UnknownEffect of UnknownReason

    type private Resolvers =
        { Resolve: Owner -> string list -> range -> Resolution option
          Storage: Owner -> string list -> range -> StorageSummary
          Parameters: Map<int * int, (string * bool) list>
          ArgumentIndex: Map<(int * int) * string, int>
          ReturnAliases: Map<int * int, string>
          PureConstructors: Set<string list * string>
          Owners: Map<int * int, Owner> }

    let private ambientMembers =
        Map.ofList (
            [ for name in
                  [ "DateTime.Now"
                    "DateTime.UtcNow"
                    "DateTime.Today"
                    "DateTimeOffset.Now"
                    "DateTimeOffset.UtcNow"
                    "Environment.TickCount"
                    "Environment.TickCount64"
                    "Environment.GetEnvironmentVariable"
                    "Environment.GetEnvironmentVariables"
                    "Environment.GetCommandLineArgs"
                    "Environment.CurrentDirectory"
                    "Environment.MachineName"
                    "Environment.UserName"
                    "Guid.NewGuid"
                    "Random.Shared"
                    "Stopwatch.GetTimestamp"
                    "Stopwatch.StartNew"
                    "Console.ReadLine"
                    "Console.Read"
                    "Console.ReadKey"
                    "Console.In"
                    "File.ReadAllText"
                    "File.ReadAllLines"
                    "File.ReadAllBytes"
                    "File.ReadLines"
                    "File.Exists"
                    "File.OpenRead"
                    "File.OpenText"
                    "Directory.Exists"
                    "Directory.GetFiles"
                    "Directory.GetDirectories"
                    "Directory.EnumerateFiles"
                    "Directory.EnumerateDirectories"
                    "Directory.GetCurrentDirectory" ] -> name, InputFlow ]
            @ [ for name in
                    [ "Console.Write"
                      "Console.WriteLine"
                      "Console.Error"
                      "Console.Out"
                      "Console.Clear"
                      "File.WriteAllText"
                      "File.WriteAllLines"
                      "File.WriteAllBytes"
                      "File.AppendAllText"
                      "File.AppendAllLines"
                      "File.Delete"
                      "File.Copy"
                      "File.Move"
                      "File.Create"
                      "File.OpenWrite"
                      "Directory.CreateDirectory"
                      "Directory.Delete"
                      "Directory.Move"
                      "Debug.Write"
                      "Debug.WriteLine"
                      "Trace.Write"
                      "Trace.WriteLine"
                      "Environment.SetEnvironmentVariable" ] -> name, OutputFlow ]
        )

    let private ambientFunctions =
        Map.ofList (
            ("stdin", InputFlow)
            :: [ for name in
                     [ "printf"
                       "printfn"
                       "eprintf"
                       "eprintfn"
                       "fprintf"
                       "fprintfn"
                       "stdout"
                       "stderr" ] -> name, OutputFlow ]
        )

    let private namespaceSegments = set [ "System"; "IO"; "Diagnostics" ]

    let private containerTypes =
        set
            [ "ResizeArray"
              "Dictionary"
              "HashSet"
              "Queue"
              "Stack"
              "SortedDictionary"
              "SortedSet"
              "LinkedList"
              "ConcurrentDictionary"
              "ConcurrentQueue"
              "ConcurrentStack"
              "ConcurrentBag"
              "StringBuilder" ]

    let private mutatingMethods =
        set
            [ "Add"
              "AddRange"
              "Remove"
              "RemoveAt"
              "RemoveAll"
              "Clear"
              "Insert"
              "Push"
              "Pop"
              "Enqueue"
              "Dequeue"
              "TryAdd"
              "TryRemove"
              "Append"
              "AppendLine"
              "Sort"
              "Reverse" ]

    let private location fileName (range: range) =
        { File = fileName
          StartLine = max 1 range.StartLine
          StartColumn = max 1 (range.StartColumn + 1)
          EndLine = max 1 range.EndLine
          EndColumn = max 1 (range.EndColumn + 1) }

    let private key (range: range) = range.StartLine, range.StartColumn

    let private names (identifiers: Ident list) =
        identifiers |> List.map (fun identifier -> identifier.idText)

    let private boundedPath identifiers = identifiers |> List.truncate 4

    let rec private isContainerType annotation =
        match annotation with
        | SynType.Array _ -> true
        | SynType.App(typeName = inner) -> isContainerType inner
        | SynType.LongIdent(SynLongIdent(id = identifiers)) ->
            identifiers
            |> List.tryLast
            |> Option.exists (fun identifier -> containerTypes.Contains identifier.idText)
        | _ -> false

    let rec private createsContainer expression =
        match expression with
        | SynExpr.ArrayOrList(isArray = true)
        | SynExpr.ArrayOrListComputed(isArray = true) -> true
        | SynExpr.Typed(expr = inner; targetType = annotation) -> isContainerType annotation || createsContainer inner
        | SynExpr.App(funcExpr = inner)
        | SynExpr.TypeApp(expr = inner) -> createsContainer inner
        | SynExpr.Ident identifier -> containerTypes.Contains identifier.idText
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = identifiers)) ->
            identifiers
            |> List.tryLast
            |> Option.exists (fun identifier -> containerTypes.Contains identifier.idText)
        | SynExpr.New(targetType = annotation) -> isContainerType annotation
        | _ -> false

    let rec private createsArray expression =
        match expression with
        | SynExpr.ArrayOrList(isArray = true)
        | SynExpr.ArrayOrListComputed(isArray = true) -> true
        | SynExpr.Typed(expr = inner)
        | SynExpr.Paren(expr = inner) -> createsArray inner
        | _ -> false

    let private createsRef expression =
        match expression with
        | SynExpr.App(funcExpr = SynExpr.Ident identifier) -> identifier.idText = "ref"
        | _ -> false

    /// Names bound by a parameter or value pattern, each flagged when annotated as a mutable container.
    let rec private patternNames (container: bool) pattern =
        match pattern with
        | SynPat.Named(SynIdent(identifier, _), _, _, _)
        | SynPat.OptionalVal(identifier, _)
        | SynPat.LongIdent(longDotId = SynLongIdent(id = [ identifier ]); argPats = SynArgPats.Pats []) ->
            [ identifier.idText, container ]
        | SynPat.Typed(pat = nested; targetType = annotation) -> patternNames (isContainerType annotation) nested
        | SynPat.Paren(pat = nested)
        | SynPat.Attrib(pat = nested) -> patternNames container nested
        | SynPat.Tuple(elementPats = elements) -> elements |> List.collect (patternNames container)
        | _ -> []

    let private isFunctionBinding (SynBinding(headPat = pattern; expr = body)) =
        match pattern, body with
        | SynPat.LongIdent(argPats = SynArgPats.Pats(_ :: _)), _
        | _, SynExpr.Lambda _
        | _, SynExpr.MatchLambda _
        | _, SynExpr.Typed(expr = SynExpr.Lambda _ | SynExpr.MatchLambda _) -> true
        | _ -> false

    let private ownsFlows binding parent =
        match parent with
        | SyntaxNode.SynMemberDefn(SynMemberDefn.Member _ | SynMemberDefn.GetSetMember _) -> true
        | SyntaxNode.SynModule(SynModuleDecl.Let _)
        | SyntaxNode.SynMemberDefn(SynMemberDefn.LetBindings _) -> isFunctionBinding binding
        | _ -> false

    let private typeKeyOf (ancestors: SyntaxNode list) =
        ancestors
        |> List.tryPick (fun node ->
            match node with
            | SyntaxNode.SynTypeDefn(SynTypeDefn(range = typeRange)) -> Some(key typeRange)
            | _ -> None)

    let private bindingKey (SynBinding(headPat = pattern)) = key pattern.Range

    /// Names of the nested modules enclosing a node, outermost first.
    let private modulePathOf (ancestors: SyntaxNode list) =
        ancestors
        |> List.rev
        |> List.collect (fun node ->
            match node with
            | SyntaxNode.SynModule(SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = identifiers))) ->
                names identifiers
            | _ -> [])

    let private owner (SynBinding(headPat = pattern) as binding) ancestors =
        let name, self =
            match pattern with
            | SynPat.LongIdent(longDotId = SynLongIdent(id = [ self; name ])) -> name.idText, Some self.idText
            | SynPat.LongIdent(longDotId = SynLongIdent(id = [ name ])) -> name.idText, None
            | _ ->
                (patternNames false pattern
                 |> List.tryHead
                 |> Option.map fst
                 |> Option.defaultValue ""),
                None

        { Name = name
          Line = pattern.Range.StartLine
          Key = bindingKey binding
          TypeKey = typeKeyOf ancestors
          Self = self
          Path = modulePathOf ancestors }

    let private ownerOf (nodes: SyntaxNode list) =
        let rec search nodes =
            match nodes with
            | SyntaxNode.SynExpr(SynExpr.Lambda(range = lambdaRange)) :: ancestors ->
                Some
                    { Key = key lambdaRange
                      Name = "<lambda>"
                      Line = lambdaRange.StartLine
                      TypeKey = typeKeyOf ancestors
                      Self = None
                      Path = modulePathOf ancestors }
            | SyntaxNode.SynBinding binding :: (parent :: _ as ancestors) when ownsFlows binding parent ->
                Some(owner binding ancestors)
            | _ :: ancestors -> search ancestors
            | [] -> None

        search nodes

    let rec private rootNames expression =
        match expression with
        | SynExpr.Ident identifier -> [ identifier.idText ]
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = identifiers)) -> names identifiers
        | SynExpr.DotGet(expr = inner; longDotId = SynLongIdent(id = identifiers)) ->
            rootNames inner @ names identifiers
        | SynExpr.App(funcExpr = inner)
        | SynExpr.DotIndexedGet(objectExpr = inner)
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner) -> rootNames inner
        | _ -> []

    let private writeTarget expression =
        match expression with
        | SynExpr.Set(targetExpr = target)
        | SynExpr.DotSet(targetExpr = target)
        | SynExpr.DotIndexedSet(objectExpr = target) -> Some target
        | SynExpr.App(
            funcExpr = SynExpr.App(
                funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ operator ])); argExpr = target)) when
            operator.idText = "op_ColonEquals"
            ->
            Some target
        | SynExpr.App(
            funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ arrayModule; operation ])); argExpr = target) when
            arrayModule.idText = "Array" && operation.idText = "set"
            ->
            Some target
        | _ -> None

    let rec private expressionContains identifiers expression =
        rootNames expression = identifiers
        || match expression with
           | SynExpr.App(funcExpr = func; argExpr = argument) ->
               expressionContains identifiers func || expressionContains identifiers argument
           | SynExpr.DotGet(expr = inner)
           | SynExpr.DotIndexedGet(objectExpr = inner)
           | SynExpr.Paren(expr = inner)
           | SynExpr.Typed(expr = inner)
           | SynExpr.TypeApp(expr = inner) -> expressionContains identifiers inner
           | SynExpr.Tuple(exprs = items)
           | SynExpr.ArrayOrList(exprs = items) -> items |> List.exists (expressionContains identifiers)
           | SynExpr.IfThenElse(ifExpr = condition; thenExpr = thenExpr; elseExpr = elseExpr) ->
               expressionContains identifiers condition
               || match literalBool condition with
                  | Some true -> expressionContains identifiers thenExpr
                  | Some false -> elseExpr |> Option.exists (expressionContains identifiers)
                  | None ->
                      expressionContains identifiers thenExpr
                      || (elseExpr |> Option.exists (expressionContains identifiers))
           | _ -> false

    and private literalBool (expression: SynExpr) =
        match expression with
        | SynExpr.Const(SynConst.Bool value, _) -> Some value
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner) -> literalBool inner
        | _ -> None

    let private writeAccess expression =
        match expression with
        | SynExpr.Set(targetExpr = target; rhsExpr = rhs)
        | SynExpr.DotSet(targetExpr = target; rhsExpr = rhs) ->
            let identifiers = rootNames target
            Some(Write(identifiers, expressionContains identifiers rhs))
        | SynExpr.DotIndexedSet(objectExpr = target) -> Some(Write(rootNames target, false))
        | SynExpr.App(
            funcExpr = SynExpr.App(
                funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ operator ])); argExpr = target)
            argExpr = rhs) when operator.idText = "op_ColonEquals" ->
            let identifiers = rootNames target
            Some(Write(identifiers, expressionContains identifiers rhs))
        | SynExpr.App(
            funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ arrayModule; operation ])); argExpr = target) when
            arrayModule.idText = "Array" && operation.idText = "set"
            ->
            Some(Write(rootNames target, false))
        | _ -> None

    let private isWriteTarget (ancestors: SyntaxNode list) (nodeRange: range) =
        ancestors
        |> List.exists (fun node ->
            match node with
            | SyntaxNode.SynExpr expression ->
                writeTarget expression
                |> Option.exists (fun target -> Range.rangeContainsRange target.Range nodeRange)
            | _ -> false)

    /// `name` in `Method(name = value)`: the left side of `=` in a parenthesised argument of an atomic application.
    let private isNamedArgument (ancestors: SyntaxNode list) (nodeRange: range) =
        let rec isArgument nodes =
            match nodes with
            | SyntaxNode.SynExpr(SynExpr.Tuple _) :: rest -> isArgument rest
            | SyntaxNode.SynExpr(SynExpr.Paren _) :: SyntaxNode.SynExpr(SynExpr.App(flag = ExprAtomicFlag.Atomic)) :: _ ->
                true
            | _ -> false

        match ancestors with
        | SyntaxNode.SynExpr(SynExpr.App(
            isInfix = true; funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ operator ])); argExpr = name)) :: SyntaxNode.SynExpr(SynExpr.App _) :: rest ->
            operator.idText = "op_Equality"
            && Range.equals name.Range nodeRange
            && isArgument rest
        | _ -> false

    /// Ambient access at the start of a name, after any namespace qualifiers.
    let private ambientAccess identifiers =
        let found table name =
            table
            |> Map.tryFind name
            |> Option.map (fun direction -> Ambient(direction, name, identifiers))

        match identifiers |> List.skipWhile namespaceSegments.Contains with
        | typeName :: memberName :: _ when ambientMembers.ContainsKey(typeName + "." + memberName) ->
            found ambientMembers (typeName + "." + memberName)
        | name :: _ -> found ambientFunctions name
        | [] -> None

    let private longIdentAccess ancestors nodeRange identifiers =
        match ambientAccess identifiers, identifiers with
        | Some ambient, _ -> Some ambient
        | None, _ when isWriteTarget ancestors nodeRange -> None
        | None, _ :: _ :: _ when mutatingMethods.Contains(List.last identifiers) ->
            Some(MutatingCall(List.take (identifiers.Length - 1) identifiers))
        | None, _ -> Some(Read identifiers)

    let private expressionAccess ancestors expression =
        match expression with
        | SynExpr.Ident identifier ->
            match ambientAccess [ identifier.idText ] with
            | Some ambient -> Some ambient
            | None when
                isWriteTarget ancestors identifier.idRange
                || isNamedArgument ancestors identifier.idRange
                ->
                None
            | None -> Some(Read [ identifier.idText ])
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = identifiers)) ->
            longIdentAccess ancestors expression.Range (names identifiers)
        | SynExpr.LongIdentSet(longDotId = SynLongIdent(id = identifiers)) ->
            match ambientAccess (names identifiers) with
            | Some(Ambient(_, name, _)) -> Some(Ambient(OutputFlow, name, names identifiers))
            | _ -> Some(Write(names identifiers, false))
        | SynExpr.DotSet(targetExpr = target; longDotId = SynLongIdent(id = identifiers)) ->
            Some(Write(rootNames target @ names identifiers, false))
        | SynExpr.App(funcExpr = SynExpr.Ident operation; argExpr = target) when
            operation.idText = "incr" || operation.idText = "decr"
            ->
            Some(Write(rootNames target, true))
        | _ -> writeAccess expression

    let rec private storageExpr expression =
        match expression with
        | SynExpr.App(
            funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ arrayModule; copyFunction ])); argExpr = source) when
            arrayModule.idText = "Array" && copyFunction.idText = "copy"
            ->
            match rootNames source with
            | [] -> None
            | identifiers -> Some(ShallowCopyStorage identifiers)
        | SynExpr.IfThenElse(ifExpr = condition; thenExpr = thenExpr; elseExpr = elseExpr) ->
            match literalBool condition, elseExpr with
            | Some true, _ -> storageExpr thenExpr
            | Some false, Some elseExpr -> storageExpr elseExpr
            | Some false, None -> None
            | None, Some elseExpr ->
                [ storageExpr thenExpr; storageExpr elseExpr ]
                |> List.choose id
                |> function
                    | [] -> None
                    | [ single ] -> Some single
                    | items -> Some(JoinedStorage items)
            | None, None -> storageExpr thenExpr
        | SynExpr.App _ ->
            let rec parts expr =
                match expr with
                | SynExpr.App(funcExpr = func; argExpr = argument) ->
                    let root, arguments = parts func
                    root, arguments @ [ argument ]
                | SynExpr.TypeApp(expr = inner)
                | SynExpr.Paren(expr = inner)
                | SynExpr.Typed(expr = inner) -> parts inner
                | _ -> expr, []

            let func, arguments = parts expression

            match rootNames func with
            | [] -> None
            | identifiers -> Some(CallResultStorage(identifiers, arguments |> List.map rootNames))
        | _ when createsArray expression -> Some FreshStorage
        | _ ->
            match rootNames expression with
            | [] -> None
            | identifiers -> Some(DirectStorage identifiers)

    let private lambdaOwner (expression: SynExpr) =
        match expression with
        | SynExpr.Lambda(range = lambdaRange) ->
            Some
                { Key = key lambdaRange
                  Name = "<lambda>"
                  Line = lambdaRange.StartLine
                  TypeKey = None
                  Self = None
                  Path = [] }
        | SynExpr.Typed(expr = SynExpr.Lambda(range = lambdaRange))
        | SynExpr.Paren(expr = SynExpr.Lambda(range = lambdaRange)) ->
            Some
                { Key = key lambdaRange
                  Name = "<lambda>"
                  Line = lambdaRange.StartLine
                  TypeKey = None
                  Self = None
                  Path = [] }
        | _ -> None

    let private callArgument expression =
        match lambdaOwner expression with
        | Some owner -> ArgumentCallable owner
        | None ->
            match rootNames expression with
            | [] -> ArgumentOther
            | identifiers -> ArgumentPath identifiers

    let rec private applicationParts expression =
        match expression with
        | SynExpr.App(funcExpr = func; argExpr = argument) ->
            let root, arguments = applicationParts func
            root, arguments @ [ argument ]
        | SynExpr.TypeApp(expr = inner)
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner) -> applicationParts inner
        | _ -> expression, []

    let private isNestedApplicationFunction (ancestors: SyntaxNode list) expressionRange =
        ancestors
        |> List.exists (fun node ->
            match node with
            | SyntaxNode.SynExpr(SynExpr.App(funcExpr = func)) -> Range.rangeContainsRange func.Range expressionRange
            | _ -> false)

    let private isReachableExpression (ancestors: SyntaxNode list) (expressionRange: range) =
        ancestors
        |> List.forall (fun node ->
            match node with
            | SyntaxNode.SynExpr(SynExpr.IfThenElse(ifExpr = condition; thenExpr = thenExpr; elseExpr = elseExpr)) ->
                match literalBool condition with
                | Some false when Range.rangeContainsRange thenExpr.Range expressionRange -> false
                | Some true when
                    elseExpr
                    |> Option.exists (fun elseExpr -> Range.rangeContainsRange elseExpr.Range expressionRange)
                    ->
                    false
                | _ -> true
            | _ -> true)

    let private accessRange expression =
        match expression with
        | SynExpr.LongIdentSet(longDotId = target) -> target.Range
        | _ ->
            writeTarget expression
            |> Option.map _.Range
            |> Option.defaultValue expression.Range

    let private expressionFacts current ancestors (expression: SynExpr) =
        if not (isReachableExpression ancestors expression.Range) then
            []
        else
            match expression with
            | SynExpr.For(ident = identifier; doBody = body) -> [ Bound(current.Key, identifier.idText, body.Range) ]
            | SynExpr.App _ when not (isNestedApplicationFunction ancestors expression.Range) ->
                let access =
                    expressionAccess ancestors expression
                    |> Option.toList
                    |> List.map (fun access -> Candidate(current, access, accessRange expression))

                let call =
                    let func, arguments = applicationParts expression

                    match rootNames func with
                    | [] -> []
                    | identifiers ->
                        [ Candidate(current, Call(identifiers, arguments |> List.map callArgument), func.Range) ]

                access @ call
            | _ ->
                expressionAccess ancestors expression
                |> Option.map (fun access -> Candidate(current, access, accessRange expression))
                |> Option.toList

    /// Where a local pattern's names are visible; None for the owner's own parameters, which are already facts.
    let private patternScope current (pattern: SynPat) (ancestors: SyntaxNode list) =
        let rec search nodes =
            match nodes with
            | SyntaxNode.SynBinding binding :: _ when bindingKey binding = current.Key -> None
            | SyntaxNode.SynBinding(SynBinding(
                headPat = SynPat.LongIdent(argPats = SynArgPats.Pats(_ :: _)) as head; expr = body)) :: _ when
                not (Range.equals head.Range pattern.Range)
                ->
                Some body.Range
            | SyntaxNode.SynExpr(SynExpr.LetOrUse letOrUse) :: _ when letOrUse.IsRecursive -> Some letOrUse.Range
            | SyntaxNode.SynExpr(SynExpr.LetOrUse letOrUse) :: _ -> Some letOrUse.Body.Range
            | SyntaxNode.SynExpr(SynExpr.ForEach(bodyExpr = body)) :: _ -> Some body.Range
            | SyntaxNode.SynMatchClause clause :: _ -> Some clause.Range
            | SyntaxNode.SynExpr expression :: _ -> Some expression.Range
            | _ :: ancestors -> search ancestors
            | [] -> None

        search ancestors

    let private patternFacts current ancestors pattern =
        match pattern, patternScope current pattern ancestors with
        | SynPat.Named(SynIdent(identifier, _), _, _, _), Some scope
        | SynPat.OptionalVal(identifier, _), Some scope
        | SynPat.LongIdent(longDotId = SynLongIdent(id = [ identifier ]); argPats = SynArgPats.Pats []), Some scope ->
            [ Bound(current.Key, identifier.idText, scope) ]
        | SynPat.LongIdent(longDotId = SynLongIdent(id = [ identifier ])), Some scope ->
            match ancestors with
            | SyntaxNode.SynBinding(SynBinding(headPat = head)) :: _ when Range.equals head.Range pattern.Range ->
                [ Bound(current.Key, identifier.idText, scope) ]
            | _ -> []
        | _ -> []

    let private parameterFacts current (SynBinding(headPat = pattern)) =
        match pattern with
        | SynPat.LongIdent(argPats = SynArgPats.Pats arguments) ->
            arguments
            |> List.collect (patternNames false)
            |> List.mapi (fun index (name, container) -> Parameter(current.Key, name, container, index))
        | _ -> []

    let private dataFacts (SynBinding(headPat = pattern; isMutable = isMutable; expr = body)) parent ancestors =
        let container = createsContainer body

        let tracked = (container && not (createsArray body)) || isMutable || createsRef body

        let moduleData () =
            patternNames container pattern
            |> List.map (fun (name, isContainer) ->
                ModuleData(
                    modulePathOf ancestors,
                    name,
                    tracked || (isContainer && not (createsArray body)),
                    isContainer,
                    key pattern.Range
                ))

        match parent, typeKeyOf ancestors with
        | SyntaxNode.SynModule(SynModuleDecl.Let _), _ -> moduleData ()
        | SyntaxNode.SynMemberDefn(SynMemberDefn.LetBindings(isStatic = true)), _ when tracked -> moduleData ()
        | SyntaxNode.SynMemberDefn(SynMemberDefn.LetBindings _), Some typeKey ->
            patternNames container pattern
            |> List.map (fun (name, isContainer) -> TypeData(typeKey, name, isContainer))
        | _ -> []

    let private nodeFacts (path: SyntaxNode list) node =
        match node, path, ownerOf (node :: path) with
        | SyntaxNode.SynBinding binding, _, Some current when current.Key = bindingKey binding ->
            let parameters = parameterFacts current binding

            let returnedAlias =
                match binding with
                | SynBinding(expr = body) ->
                    match rootNames body with
                    | [ name ] when
                        parameters
                        |> List.exists (function
                            | Parameter(_, parameterName, _, _) -> parameterName = name
                            | _ -> false)
                        ->
                        [ ReturnAlias(current.Key, name) ]
                    | _ -> []

            let returnedCallable =
                match binding with
                | SynBinding(expr = body) ->
                    lambdaOwner body
                    |> Option.map (fun callable -> ReturnCallable(current.Key, callable.Key))
                    |> Option.toList

            (Callable current :: parameters) @ returnedAlias @ returnedCallable
        | SyntaxNode.SynBinding binding, parent :: _, None -> dataFacts binding parent path
        | SyntaxNode.SynTypeDefn(SynTypeDefn(typeInfo = SynComponentInfo(longId = identifiers))), _, _ ->
            identifiers
            |> List.tryLast
            |> Option.map (fun identifier -> PureConstructor(modulePathOf path, identifier.idText))
            |> Option.toList
        | SyntaxNode.SynBinding(SynBinding(headPat = pattern; expr = expression) as binding), _, Some current when
            current.Key <> bindingKey binding
            ->
            let scope =
                patternScope current pattern path |> Option.defaultValue expression.Range

            let source =
                match expression with
                | SynExpr.App _ -> []
                | _ -> rootNames expression

            let aliasFacts =
                match source with
                | [] -> []
                | _ ->
                    patternNames false pattern
                    |> List.choose (fun (name, _) ->
                        match source with
                        | root :: _ when root = name -> None
                        | _ -> Some(Alias(current.Key, name, source, scope)))

            let storageFacts =
                match storageExpr expression with
                | Some storage ->
                    patternNames false pattern
                    |> List.map (fun (name, _) -> Storage(current.Key, name, storage, scope))
                | None -> []

            let callableStorageFacts =
                match expression with
                | SynExpr.App _ ->
                    let func, _ = applicationParts expression

                    match rootNames func with
                    | [] -> []
                    | callee ->
                        patternNames false pattern
                        |> List.map (fun (name, _) -> CallableStorage(current.Key, name, callee, scope))
                | _ -> []

            let boundFacts =
                patternNames false pattern
                |> List.collect (fun (name, _) ->
                    [ Bound(current.Key, name, scope); LocalName(current.Key, name, key scope) ])

            boundFacts @ aliasFacts @ storageFacts @ callableStorageFacts
        | SyntaxNode.SynExpr(SynExpr.Lambda _), _, Some current -> [ Callable current ]
        | SyntaxNode.SynModule(SynModuleDecl.Open(
            target = SynOpenDeclTarget.ModuleOrNamespace(longId = SynLongIdent(id = identifiers)))),
          _,
          _ -> [ Open(modulePathOf path, names identifiers, key node.Range) ]
        | SyntaxNode.SynMemberDefn(SynMemberDefn.ImplicitCtor(ctorArgs = arguments)), _, _ ->
            match typeKeyOf path with
            | Some typeKey ->
                patternNames false arguments
                |> List.map (fun (name, container) -> TypeData(typeKey, name, container))
            | None -> []
        | SyntaxNode.SynMemberDefn(SynMemberDefn.AutoProperty(ident = identifier; synExpr = body)), _, _ ->
            typeKeyOf path
            |> Option.map (fun typeKey -> TypeProperty(typeKey, identifier.idText, createsContainer body))
            |> Option.toList
        | SyntaxNode.SynMemberDefn(SynMemberDefn.Member(
            memberDefn = SynBinding(
                headPat = SynPat.LongIdent(
                    longDotId = SynLongIdent(id = [ _; identifier ]); argPats = SynArgPats.Pats(_ :: _))))),
          _,
          _ ->
            typeKeyOf path
            |> Option.map (fun typeKey -> TypeMethod(typeKey, identifier.idText))
            |> Option.toList
        | SyntaxNode.SynPat pattern, _, Some current -> patternFacts current path pattern
        | SyntaxNode.SynExpr expression, _, Some current -> expressionFacts current path expression
        | _ -> []

    let rec private enclosing path =
        match path with
        | [] -> [ [] ]
        | _ -> path :: enclosing (List.take (path.Length - 1) path)

    /// Innermost module value an access names, choosing between module declarations
    /// and opened values by source position: the later one wins, within the owner's enclosing modules.
    let private moduleValue moduleData opens current (identifiers: string list) accessRange =
        let accessPos = key accessRange

        let candidateIn encPath =
            let localCandidate =
                [ 0 .. identifiers.Length - 1 ]
                |> List.tryPick (fun qualifiers ->
                    let targetPath = encPath @ List.take qualifiers identifiers
                    let valueName = identifiers[qualifiers]

                    moduleData
                    |> Map.tryFind (targetPath, valueName)
                    |> Option.bind (fun items -> items |> List.tryFind (fun (_, _, pos) -> pos <= accessPos))
                    |> Option.map (fun (tracked, container, pos) ->
                        String.concat "." (List.take (qualifiers + 1) identifiers), (tracked, container, pos), pos))

            let openCandidate (target, openPos) =
                let openTargets = enclosing encPath |> List.map (fun prefix -> prefix @ target)

                List.allPairs openTargets [ 0 .. identifiers.Length - 1 ]
                |> List.tryPick (fun (openTarget, qualifiers) ->
                    let targetPath = openTarget @ List.take qualifiers identifiers
                    let valueName = identifiers[qualifiers]

                    moduleData
                    |> Map.tryFind (targetPath, valueName)
                    |> Option.bind (fun items -> items |> List.tryFind (fun (_, _, pos) -> pos <= accessPos))
                    |> Option.map (fun (tracked, container, pos) ->
                        String.concat "." (List.take (qualifiers + 1) identifiers), (tracked, container, pos), openPos))

            let openCandidates =
                opens
                |> List.filter (fun (path, _, openPos) -> path = encPath && openPos <= accessPos)
                |> List.choose (fun (_, target, openPos) -> openCandidate (target, openPos))

            let allCandidates =
                match localCandidate with
                | Some local -> local :: openCandidates
                | None -> openCandidates

            match allCandidates with
            | [] -> None
            | candidates ->
                let resolvedName, value, _ = candidates |> List.maxBy (fun (_, _, pos) -> pos)
                Some(resolvedName, value)

        enclosing current.Path |> List.tryPick candidateIn

    let private emptyStorage =
        { Outer = Set.empty
          Reachable = Set.empty
          Complete = true }

    let private unknownStorage name =
        { Outer = Set.singleton (UnknownOrigin name)
          Reachable = Set.singleton (UnknownOrigin name)
          Complete = false }

    let private freshStorage =
        { Outer = Set.singleton FreshOrigin
          Reachable = Set.singleton FreshOrigin
          Complete = true }

    let private joinStorage storages =
        { Outer = storages |> List.collect (fun item -> Set.toList item.Outer) |> Set.ofList
          Reachable = storages |> List.collect (fun item -> Set.toList item.Reachable) |> Set.ofList
          Complete = storages |> List.forall _.Complete }

    let private resolver (facts: Fact list) =
        let owners =
            facts
            |> List.choose (function
                | Callable owner -> Some(owner.Key, owner)
                | _ -> None)
            |> Map.ofList

        let callableByName =
            facts
            |> List.choose (function
                | Callable owner -> Some((owner.Path, owner.Name), owner)
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (name, entries) -> name, entries |> List.map snd |> List.sortBy (fun owner -> owner.Line))
            |> Map.ofList

        let parameters =
            facts
            |> List.choose (function
                | Parameter(ownerKey, name, container, index) -> Some(ownerKey, (index, name, container))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (ownerKey, entries) ->
                ownerKey,
                entries
                |> List.map snd
                |> List.sortBy (fun (index, _, _) -> index)
                |> List.map (fun (_, name, container) -> name, container))
            |> Map.ofList

        let argumentIndex =
            facts
            |> List.choose (function
                | Parameter(ownerKey, name, _, index) -> Some((ownerKey, name), index)
                | _ -> None)
            |> Map.ofList

        let parameterContainers =
            parameters
            |> Map.toList
            |> List.collect (fun (ownerKey, items) ->
                items |> List.map (fun (name, container) -> (ownerKey, name), container))
            |> Map.ofList

        let aliases =
            facts
            |> List.choose (function
                | Alias(ownerKey, name, source, scope) -> Some((ownerKey, name), (source, scope))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, entries) -> binding, List.map snd entries)
            |> Map.ofList

        let returnAliases =
            facts
            |> List.choose (function
                | ReturnAlias(ownerKey, name) -> Some(ownerKey, name)
                | _ -> None)
            |> Map.ofList

        let returnCallables =
            facts
            |> List.choose (function
                | ReturnCallable(ownerKey, callableKey) -> Some(ownerKey, callableKey)
                | _ -> None)
            |> Map.ofList

        let storages =
            facts
            |> List.choose (function
                | Storage(ownerKey, name, storage, scope) -> Some((ownerKey, name), (storage, scope))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, entries) -> binding, List.map snd entries)
            |> Map.ofList

        let callableStorages =
            facts
            |> List.choose (function
                | CallableStorage(ownerKey, name, callee, scope) -> Some((ownerKey, name), (callee, scope))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, entries) -> binding, List.map snd entries)
            |> Map.ofList

        let bound =
            facts
            |> List.choose (function
                | Bound(ownerKey, name, scope) -> Some((ownerKey, name), scope)
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, scopes) -> binding, List.map snd scopes)
            |> Map.ofList

        let locals =
            facts
            |> List.choose (function
                | LocalName(ownerKey, name, pos) -> Some((ownerKey, name), pos)
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, positions) -> binding, List.map snd positions)
            |> Map.ofList

        let properties =
            facts
            |> List.choose (function
                | TypeProperty(typeKey, name, container) -> Some((typeKey, name), container)
                | _ -> None)
            |> Map.ofList

        let methods =
            facts
            |> List.choose (function
                | TypeMethod(typeKey, name) -> Some(typeKey, name)
                | _ -> None)
            |> Set.ofList

        let typeData =
            facts
            |> List.choose (function
                | TypeData(typeKey, name, container) -> Some((typeKey, name), container)
                | _ -> None)
            |> Map.ofList

        let moduleData =
            facts
            |> List.choose (function
                | ModuleData(path, name, tracked, container, pos) -> Some((path, name), (tracked, container, pos))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (key, items) ->
                key, items |> List.map snd |> List.sortByDescending (fun (_, _, pos) -> pos))
            |> Map.ofList

        let opens =
            facts
            |> List.choose (function
                | Open(path, target, pos) -> Some(path, target, pos)
                | _ -> None)

        let pureConstructors =
            facts
            |> List.choose (function
                | PureConstructor(path, name) -> Some(path, name)
                | _ -> None)
            |> Set.ofList

        let writtenContainers =
            facts
            |> List.choose (function
                | Candidate(current, Write(identifiers, _), accessRange) ->
                    moduleValue moduleData opens current identifiers accessRange
                    |> Option.bind (fun (_, (_, container, position)) -> if container then Some position else None)
                | _ -> None)
            |> Set.ofList

        let isLocal current name accessRange =
            bound
            |> Map.tryFind (current.Key, name)
            |> Option.exists (List.exists (fun scope -> Range.rangeContainsRange scope accessRange))
            || locals
               |> Map.tryFind (current.Key, name)
               |> Option.exists (List.exists (fun pos -> pos <= key accessRange))

        let callable current identifiers (accessRange: range) =
            match identifiers with
            | [ name ] ->
                enclosing current.Path
                |> List.tryPick (fun path ->
                    callableByName
                    |> Map.tryFind (path, name)
                    |> Option.bind (fun candidates ->
                        candidates
                        |> List.sortByDescending (fun candidate -> candidate.Line)
                        |> List.tryHead))
            | _ -> None

        let rec resolve visited current identifiers accessRange =
            let callableStorage =
                match identifiers with
                | [ root ] when not (Set.contains root visited) ->
                    callableStorages
                    |> Map.tryFind (current.Key, root)
                    |> Option.bind (List.tryFind (fun (_, scope) -> Range.rangeContainsRange scope accessRange))
                    |> Option.bind (fun (callee, _) ->
                        match resolve (Set.add root visited) current callee accessRange with
                        | Some(CallableTarget target) ->
                            returnCallables
                            |> Map.tryFind target.Key
                            |> Option.bind (fun callableKey -> owners |> Map.tryFind callableKey)
                            |> Option.map CallableTarget
                        | _ -> None)
                | _ -> None

            let alias =
                match identifiers with
                | root :: _ when isLocal current root accessRange -> None
                | root :: tail when not (Set.contains root visited) ->
                    Map.tryFind (current.Key, root) aliases
                    |> Option.bind (List.tryFind (fun (_, scope) -> Range.rangeContainsRange scope accessRange))
                    |> Option.map (fun (source, _) -> source @ tail, Set.add root visited)
                | _ -> None

            match callableStorage, alias, identifiers with
            | Some target, _, _ -> Some target
            | None, Some(source, followed), _ -> resolve followed current source accessRange
            | None, None, root :: _ when isLocal current root accessRange -> Some Local
            | None, None, self :: memberName :: _ when current.Self = Some self ->
                match current.TypeKey with
                | Some typeKey when methods.Contains(typeKey, memberName) -> Local
                | typeKey ->
                    let container =
                        typeKey
                        |> Option.bind (fun typeKey -> properties |> Map.tryFind (typeKey, memberName))

                    Data(TypeState, memberName, container = Some true)
                |> Some
            | None, None, root :: _ ->
                let typeContainer =
                    current.TypeKey
                    |> Option.bind (fun typeKey -> typeData |> Map.tryFind (typeKey, root))

                match
                    parameterContainers |> Map.tryFind (current.Key, root),
                    typeContainer,
                    moduleValue moduleData opens current identifiers accessRange
                with
                | _ when isLocal current root accessRange -> Local
                | Some container, _, _ -> Argument(root, container)
                | None, Some container, _ -> Data(TypeState, root, container)
                | None, None, Some(name, (tracked, container, position)) ->
                    if tracked || Set.contains position writtenContainers then
                        Data(SharedState, name, container)
                    else
                        ModuleValue name
                | None, None, None ->
                    match callable current identifiers accessRange with
                    | Some target -> CallableTarget target
                    | None -> Unknown(String.concat "." identifiers)
                |> Some
            | None, None, [] -> None

        let rec storageOf visited current identifiers accessRange =
            match identifiers with
            | [] -> emptyStorage
            | root :: tail ->
                let parameter =
                    if isLocal current root accessRange then
                        None
                    else
                        parameterContainers |> Map.tryFind (current.Key, root)

                let fromResolution () =
                    match resolve Set.empty current [ root ] accessRange with
                    | Some(Argument(name, _)) ->
                        { Outer = Set.singleton (ParameterOrigin name)
                          Reachable = Set.singleton (ParameterOrigin name)
                          Complete = true }
                    | Some(Data(source, name, _)) ->
                        { Outer = Set.singleton (SharedOrigin(source, name))
                          Reachable = Set.singleton (SharedOrigin(source, name))
                          Complete = true }
                    | Some(ModuleValue _) -> freshStorage
                    | Some(Unknown name) -> unknownStorage name
                    | _ -> emptyStorage

                let rootStorage =
                    match parameter with
                    | Some _ ->
                        { Outer = Set.singleton (ParameterOrigin root)
                          Reachable = Set.singleton (ParameterOrigin root)
                          Complete = true }
                    | None when not (Set.contains root visited) ->
                        storages
                        |> Map.tryFind (current.Key, root)
                        |> Option.bind (List.tryFind (fun (_, scope) -> Range.rangeContainsRange scope accessRange))
                        |> Option.map (fun (storageExpr, _) ->
                            let rec eval expr =
                                match expr with
                                | DirectStorage source -> storageOf (Set.add root visited) current source accessRange
                                | ShallowCopyStorage source ->
                                    let sourceStorage = storageOf (Set.add root visited) current source accessRange

                                    { Outer = Set.singleton FreshOrigin
                                      Reachable = sourceStorage.Reachable
                                      Complete = sourceStorage.Complete }
                                | CallResultStorage(callee, arguments) ->
                                    match resolve Set.empty current callee accessRange with
                                    | Some(CallableTarget target) ->
                                        match returnAliases |> Map.tryFind target.Key with
                                        | Some parameterName ->
                                            match argumentIndex |> Map.tryFind (target.Key, parameterName) with
                                            | Some index when index < List.length arguments ->
                                                match List.item index arguments with
                                                | [] -> unknownStorage (String.concat "." callee)
                                                | source -> storageOf (Set.add root visited) current source accessRange
                                            | _ -> unknownStorage (String.concat "." callee)
                                        | None -> unknownStorage (String.concat "." callee)
                                    | Some(Unknown name) -> unknownStorage name
                                    | _ -> unknownStorage (String.concat "." callee)
                                | FreshStorage -> freshStorage
                                | JoinedStorage items -> items |> List.map eval |> joinStorage

                            eval storageExpr)
                        |> Option.defaultWith fromResolution
                    | None when isLocal current root accessRange -> freshStorage
                    | None -> fromResolution ()

                if List.isEmpty tail then
                    rootStorage
                else
                    { rootStorage with
                        Outer = rootStorage.Reachable }

        { Resolve = resolve Set.empty
          Storage = storageOf Set.empty
          Parameters = parameters
          ArgumentIndex = argumentIndex
          ReturnAliases = returnAliases
          PureConstructors = pureConstructors
          Owners = owners }

    let private flow direction source name current flowLocation =
        { Direction = direction
          Source = source
          Name = name
          Owner = current.Name
          OwnerLine = current.Line
          Location = flowLocation }

    let private isKnownPureCall (resolvers: Resolvers) current identifiers =
        let isLocalConstructor name =
            enclosing current.Path
            |> List.exists (fun path -> Set.contains (path, name) resolvers.PureConstructors)

        match identifiers with
        | [ "async" ]
        | [ "Array"; "copy" ]
        | [ "Array"; "set" ]
        | [ "Array"; "map" ]
        | [ "Array"; "length" ]
        | [ "Int32"; "Parse" ]
        | [ "Int64"; "Parse" ]
        | [ "Double"; "Parse" ]
        | [ "Decimal"; "Parse" ]
        | [ "List"; "map" ]
        | [ "List"; "fold" ]
        | [ "List"; "length" ]
        | [ "Seq"; "map" ]
        | [ "Seq"; "length" ] -> true
        | [ name ] ->
            name.StartsWith("op_", System.StringComparison.Ordinal)
            || Set.contains
                name
                (set
                    [ "id"
                      "ignore"
                      "fst"
                      "snd"
                      "not"
                      "int"
                      "string"
                      "float"
                      "decimal"
                      "abs"
                      "incr"
                      "decr"
                      "min"
                      "max"
                      "None"
                      "Some"
                      "Dictionary"
                      "ResizeArray" ])
            || isLocalConstructor name
        | _ -> false

    let private effectFromOrigin direction origin =
        match direction, origin with
        | OutputFlow, ParameterOrigin name -> Some(ParameterWrite name)
        | _, SharedOrigin(source, name) -> Some(KnownEffect(direction, source, name))
        | _, UnknownOrigin name -> Some(UnknownEffect(UnknownCall name))
        | _ -> None

    let private directEffects (resolvers: Resolvers) current access accessRange =
        let make direction source name =
            [ KnownEffect(direction, source, name) ]

        let resolve current identifiers =
            resolvers.Resolve current identifiers accessRange

        match access with
        | Ambient(direction, name, identifiers) ->
            match resolve current identifiers with
            | Some Local
            | Some(Argument _)
            | Some(ModuleValue _) -> []
            | Some(Data(source, dataName, _)) ->
                if name = "Environment.CurrentDirectory" then
                    make OutputFlow source dataName
                else
                    make InputFlow source dataName
            | Some(Unknown _)
            | None -> make direction AmbientEffect name
            | Some(CallableTarget _) -> []
        | Read identifiers ->
            match resolve current identifiers with
            | Some(Data(source, name, _)) -> make InputFlow source name
            | _ -> []
        | Write(identifiers, readsExisting) ->
            let knownInput source name =
                if readsExisting then make InputFlow source name else []

            match resolve current identifiers with
            | Some(Argument(name, _)) -> [ ParameterWrite name ]
            | Some(Data(source, name, _)) -> make OutputFlow source name @ knownInput source name
            | Some(Unknown name) -> make OutputFlow SharedState name
            | _ ->
                let storage = resolvers.Storage current identifiers accessRange

                let outputs =
                    storage.Outer |> Set.toList |> List.choose (effectFromOrigin OutputFlow)

                let inputs =
                    if readsExisting then
                        storage.Outer |> Set.toList |> List.choose (effectFromOrigin InputFlow)
                    else
                        []

                outputs @ inputs
        | MutatingCall identifiers ->
            match resolve current identifiers with
            | Some(Argument(name, true)) -> [ ParameterWrite name ]
            | Some(Data(source, name, true)) -> make OutputFlow source name
            | Some(Data(source, name, false)) -> make InputFlow source name
            | _ ->
                let storage = resolvers.Storage current identifiers accessRange

                storage.Outer |> Set.toList |> List.choose (effectFromOrigin OutputFlow)
        | Call(identifiers, _) ->
            match
                resolve current identifiers, ambientAccess identifiers, isKnownPureCall resolvers current identifiers
            with
            | _, _, _ when identifiers |> List.tryLast |> Option.exists mutatingMethods.Contains -> []
            | Some(Argument(name, _)), _, _ ->
                match access with
                | Call(_, arguments) -> [ ParameterCall(name, arguments) ]
                | _ -> []
            | _, Some _, _ -> []
            | _, _, true -> []
            | Some(CallableTarget _), _, _ -> []
            | Some(Local), _, _ -> []
            | Some(ModuleValue _), _, _ -> [ UnknownEffect(UnknownCall(String.concat "." identifiers)) ]
            | Some(Data(source, name, _)), _, _ -> make InputFlow source name
            | Some(Unknown name), _, _ -> [ UnknownEffect(UnknownCall name) ]
            | None, None, _ -> [ UnknownEffect(UnknownCall(String.concat "." identifiers)) ]

    let private materializeEffect fileName current effect effectRange =
        let make direction source name =
            Some(flow direction source name current (location fileName effectRange))

        match effect with
        | KnownEffect(direction, source, name) -> make direction source name
        | ParameterWrite name -> make OutputFlow ArgumentMutation name
        | ParameterCall(name, _) -> make UnknownFlow UnknownEffectSource name
        | UnknownEffect reason ->
            let name =
                match reason with
                | UnknownCall name -> name

            make UnknownFlow UnknownEffectSource name

    /// Implicit flows in source order, one per owner, direction, and name.
    let collect (fileName: string) (parsedInput: ParsedInput) =
        let facts =
            ParsedInput.fold (fun facts path node -> nodeFacts path node @ facts) [] parsedInput

        let resolvers = resolver facts

        let calls =
            facts
            |> List.choose (function
                | Candidate(current, Call(identifiers, arguments), accessRange) ->
                    match resolvers.Resolve current identifiers accessRange with
                    | Some(CallableTarget target) -> Some(current, target, arguments, accessRange)
                    | _ -> None
                | _ -> None)

        let addEffect ownerKey effect effectRange summaries =
            let current = summaries |> Map.tryFind ownerKey |> Option.defaultValue Map.empty

            if Map.containsKey effect current then
                summaries
            else
                summaries |> Map.add ownerKey (current |> Map.add effect effectRange)

        let initialSummaries =
            facts
            |> List.fold
                (fun summaries fact ->
                    match fact with
                    | Candidate(current, access, accessRange) ->
                        directEffects resolvers current access accessRange
                        |> List.fold (fun state effect -> addEffect current.Key effect accessRange state) summaries
                    | _ -> summaries)
                Map.empty

        let effectsFor ownerKey summaries =
            summaries |> Map.tryFind ownerKey |> Option.defaultValue Map.empty

        let effectAtIndex target paramName =
            resolvers.ArgumentIndex |> Map.tryFind (target.Key, paramName)

        let rec transferEffect summaries caller target (arguments: CallArgument list) (callRange: range) effect =
            let callableFromArgument argument =
                match argument with
                | ArgumentCallable callable -> Some callable
                | ArgumentPath identifiers ->
                    match resolvers.Resolve caller identifiers callRange with
                    | Some(CallableTarget callable) -> Some callable
                    | _ -> None
                | ArgumentOther -> None

            let forwardedParameter argument =
                match argument with
                | ArgumentPath identifiers ->
                    match resolvers.Resolve caller identifiers callRange with
                    | Some(Argument(name, _)) -> Some name
                    | _ -> None
                | _ -> None

            let substituteArgument argument =
                match argument with
                | ArgumentPath(root :: tail) ->
                    match effectAtIndex target root, arguments with
                    | Some index, _ when index < List.length arguments ->
                        match List.item index arguments with
                        | ArgumentPath actual -> ArgumentPath(boundedPath (actual @ tail))
                        | ArgumentCallable callable when List.isEmpty tail -> ArgumentCallable callable
                        | _ -> ArgumentOther
                    | _ -> ArgumentOther
                | _ -> argument

            match effect with
            | KnownEffect _ -> [ effect, callRange ]
            | ParameterCall(parameterName, invocationArguments) ->
                match effectAtIndex target parameterName, arguments with
                | Some index, _ when index < List.length arguments ->
                    let actual = List.item index arguments

                    match callableFromArgument actual with
                    | Some callable ->
                        effectsFor callable.Key summaries
                        |> Map.toList
                        |> List.collect (fun (innerEffect, _) ->
                            transferEffect summaries target callable invocationArguments callRange innerEffect)
                        |> List.collect (fun (instantiatedEffect, _) ->
                            transferEffect summaries caller target arguments callRange instantiatedEffect)
                    | None ->
                        match forwardedParameter actual with
                        | Some forwardedName ->
                            [ ParameterCall(forwardedName, invocationArguments |> List.map substituteArgument),
                              callRange ]
                        | None -> [ UnknownEffect(UnknownCall parameterName), callRange ]
                | _ -> [ ParameterCall(parameterName, invocationArguments), callRange ]
            | UnknownEffect _ -> [ effect, callRange ]
            | ParameterWrite parameterName ->
                match effectAtIndex target parameterName, arguments with
                | Some index, _ when index < List.length arguments ->
                    match List.item index arguments with
                    | ArgumentPath identifiers ->
                        let storage = resolvers.Storage caller identifiers callRange

                        storage.Outer
                        |> Set.toList
                        |> List.choose (effectFromOrigin OutputFlow)
                        |> List.map (fun transferred -> transferred, callRange)
                    | _ -> [ UnknownEffect(UnknownCall parameterName), callRange ]
                | _ -> [ effect, callRange ]

        let rec propagate iteration summaries =
            if iteration > facts.Length + 1 then
                summaries
            else
                let next =
                    calls
                    |> List.fold
                        (fun state (caller, target, arguments, callRange) ->
                            effectsFor target.Key state
                            |> Map.toList
                            |> List.collect (fun (effect, _) ->
                                transferEffect state caller target arguments callRange effect)
                            |> List.fold
                                (fun innerState (effect, effectRange) ->
                                    addEffect caller.Key effect effectRange innerState)
                                state)
                        summaries

                if next = summaries then
                    summaries
                else
                    propagate (iteration + 1) next

        let summaries = propagate 0 initialSummaries

        let produced =
            summaries
            |> Map.toList
            |> List.collect (fun (ownerKey, effects) ->
                match resolvers.Owners |> Map.tryFind ownerKey with
                | Some owner ->
                    effects
                    |> Map.toList
                    |> List.choose (fun (effect, effectRange) -> materializeEffect fileName owner effect effectRange)
                | None -> [])

        let outputLocations =
            produced
            |> List.filter (fun item -> item.Direction = OutputFlow)
            |> List.map (fun item ->
                item.OwnerLine, item.Owner, item.Source, item.Name, item.Location.StartLine, item.Location.StartColumn)
            |> Set.ofList

        produced
        |> List.filter (fun item ->
            item.Direction <> InputFlow
            || not (
                Set.contains
                    (item.OwnerLine,
                     item.Owner,
                     item.Source,
                     item.Name,
                     item.Location.StartLine,
                     item.Location.StartColumn)
                    outputLocations
            ))
        |> List.sortBy (fun item -> item.Location.StartLine, item.Location.StartColumn)
        |> List.distinctBy (fun item -> item.OwnerLine, item.Owner, item.Direction, item.Source, item.Name)
