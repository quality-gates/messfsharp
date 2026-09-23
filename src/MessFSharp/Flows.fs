namespace MessFSharp

open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Domain

/// Finds data that enters a function or member other than through its arguments,
/// or leaves it other than through its return value. Syntax only: no type information.
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyMethods")>]
module Flows =
    type private Owner =
        { Name: string
          Line: int
          Key: int * int
          TypeKey: (int * int) option
          Self: string option
          Path: string list }

    type private Access =
        | Read of string list
        | Write of string list
        | MutatingCall of string list
        | Ambient of FlowDirection * string

    type private Fact =
        | Parameter of (int * int) * string * bool
        | Bound of (int * int) * string * range
        | TypeData of (int * int) * string * bool
        | TypeProperty of (int * int) * string * bool
        | TypeMethod of (int * int) * string
        | ModuleData of path: string list * name: string * tracked: bool * container: bool
        | Open of path: string list * target: string list
        | Candidate of Owner * Access * range

    type private Resolution =
        | Argument of string * bool
        | Local
        | Data of FlowSource * string * bool
        | Unknown of string

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

    let rec private isContainerType annotation =
        match annotation with
        | SynType.App(typeName = inner) -> isContainerType inner
        | SynType.LongIdent(SynLongIdent(id = identifiers)) ->
            identifiers
            |> List.tryLast
            |> Option.exists (fun identifier -> containerTypes.Contains identifier.idText)
        | _ -> false

    let rec private createsContainer expression =
        match expression with
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

    /// The outermost function or member binding enclosing the first node; nested functions belong to it.
    let private ownerOf (nodes: SyntaxNode list) =
        let rec search nodes found =
            match nodes with
            | SyntaxNode.SynBinding binding :: (parent :: _ as ancestors) when ownsFlows binding parent ->
                search ancestors (Some(owner binding ancestors))
            | _ :: ancestors -> search ancestors found
            | [] -> found

        search nodes None

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
            |> Option.map (fun direction -> Ambient(direction, name))

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
            | Some(Ambient(_, name)) -> Some(Ambient(OutputFlow, name))
            | _ -> Some(Write(names identifiers))
        | SynExpr.DotSet(targetExpr = target; longDotId = SynLongIdent(id = identifiers)) ->
            Some(Write(rootNames target @ names identifiers))
        | SynExpr.App(funcExpr = SynExpr.Ident operation; argExpr = target) when
            operation.idText = "incr" || operation.idText = "decr"
            ->
            Some(Write(rootNames target))
        | _ -> writeTarget expression |> Option.map (rootNames >> Write)

    let private accessRange expression =
        match expression with
        | SynExpr.LongIdentSet(longDotId = target) -> target.Range
        | _ ->
            writeTarget expression
            |> Option.map _.Range
            |> Option.defaultValue expression.Range

    let private expressionFacts current ancestors expression =
        match expression with
        | SynExpr.For(ident = identifier; doBody = body) -> [ Bound(current.Key, identifier.idText, body.Range) ]
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
            |> List.map (fun (name, container) -> Parameter(current.Key, name, container))
        | _ -> []

    let private dataFacts (SynBinding(headPat = pattern; isMutable = isMutable; expr = body)) parent ancestors =
        let container = createsContainer body

        let tracked = container || isMutable || createsRef body

        let moduleData () =
            patternNames container pattern
            |> List.map (fun (name, isContainer) ->
                ModuleData(modulePathOf ancestors, name, tracked || isContainer, isContainer))

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
            parameterFacts current binding
        | SyntaxNode.SynBinding binding, parent :: _, None -> dataFacts binding parent path
        | SyntaxNode.SynModule(SynModuleDecl.Open(
            target = SynOpenDeclTarget.ModuleOrNamespace(longId = SynLongIdent(id = identifiers)))),
          _,
          _ -> [ Open(modulePathOf path, names identifiers) ]
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

    let private resolver (facts: Fact list) =
        let parameters =
            facts
            |> List.choose (function
                | Parameter(ownerKey, name, container) -> Some((ownerKey, name), container)
                | _ -> None)
            |> Map.ofList

        let bound =
            facts
            |> List.choose (function
                | Bound(ownerKey, name, scope) -> Some((ownerKey, name), scope)
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, scopes) -> binding, List.map snd scopes)
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
                | ModuleData(path, name, tracked, container) -> Some((path, name), (tracked, container))
                | _ -> None)
            |> Map.ofList

        let opens =
            facts
            |> List.choose (function
                | Open(path, target) -> Some(path, target)
                | _ -> None)

        let rec enclosing path =
            match path with
            | [] -> [ [] ]
            | _ -> path :: enclosing (List.take (path.Length - 1) path)

        /// Innermost module value an access names, from enclosing modules first, then opened modules.
        let moduleValue current (identifiers: string list) =
            let opened =
                opens
                |> List.filter (fun (path, _) -> List.truncate path.Length current.Path = path)
                |> List.collect (fun (path, target) -> enclosing path |> List.map (fun prefix -> prefix @ target))

            List.allPairs (enclosing current.Path @ opened) [ 0 .. identifiers.Length - 1 ]
            |> List.tryPick (fun (prefix, qualifiers) ->
                moduleData
                |> Map.tryFind (prefix @ List.take qualifiers identifiers, identifiers[qualifiers])
                |> Option.map (fun value -> String.concat "." (List.take (qualifiers + 1) identifiers), value))

        let isLocal current name accessRange =
            bound
            |> Map.tryFind (current.Key, name)
            |> Option.exists (List.exists (fun scope -> Range.rangeContainsRange scope accessRange))

        fun current identifiers accessRange ->
            match identifiers with
            | self :: memberName :: _ when current.Self = Some self ->
                match current.TypeKey with
                | Some typeKey when methods.Contains(typeKey, memberName) -> Local
                | typeKey ->
                    let container =
                        typeKey
                        |> Option.bind (fun typeKey -> properties |> Map.tryFind (typeKey, memberName))

                    Data(TypeState, memberName, container = Some true)
                |> Some
            | root :: _ ->
                let typeContainer =
                    current.TypeKey
                    |> Option.bind (fun typeKey -> typeData |> Map.tryFind (typeKey, root))

                match parameters |> Map.tryFind (current.Key, root), typeContainer, moduleValue current identifiers with
                | _ when isLocal current root accessRange -> Local
                | Some container, _, _ -> Argument(root, container)
                | None, Some container, _ -> Data(TypeState, root, container)
                | None, None, Some(name, (true, container)) -> Data(SharedState, name, container)
                | None, None, _ -> Unknown(String.concat "." identifiers)
                |> Some
            | [] -> None

    let private flow direction source name current flowLocation =
        { Direction = direction
          Source = source
          Name = name
          Owner = current.Name
          OwnerLine = current.Line
          Location = flowLocation }

    let private classify resolve current access fileName accessRange =
        let make direction (source, name) =
            Some(flow direction source name current (location fileName accessRange))

        let resolve current identifiers = resolve current identifiers accessRange

        match access with
        | Ambient(direction, name) -> make direction (AmbientEffect, name)
        | Read identifiers ->
            match resolve current identifiers with
            | Some(Data(source, name, _)) -> make InputFlow (source, name)
            | _ -> None
        | Write identifiers ->
            match resolve current identifiers with
            | Some(Argument(name, _)) -> make OutputFlow (ArgumentMutation, name)
            | Some(Data(source, name, _)) -> make OutputFlow (source, name)
            | Some(Unknown name) -> make OutputFlow (SharedState, name)
            | _ -> None
        | MutatingCall identifiers ->
            match resolve current identifiers with
            | Some(Argument(name, true)) -> make OutputFlow (ArgumentMutation, name)
            | Some(Data(source, name, true)) -> make OutputFlow (source, name)
            | Some(Data(source, name, false)) -> make InputFlow (source, name)
            | _ -> None

    /// Implicit flows in source order, one per owner, direction, and name.
    let collect (fileName: string) (parsedInput: ParsedInput) =
        let facts =
            ParsedInput.fold (fun facts path node -> nodeFacts path node @ facts) [] parsedInput

        let resolve = resolver facts

        facts
        |> List.choose (function
            | Candidate(current, access, accessRange) -> classify resolve current access fileName accessRange
            | _ -> None)
        |> List.sortBy (fun item -> item.Location.StartLine, item.Location.StartColumn)
        |> List.distinctBy (fun item -> item.OwnerLine, item.Owner, item.Direction, item.Source, item.Name)
