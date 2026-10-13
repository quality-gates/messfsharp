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
    type private OwnerKey =
        | FunctionOwner of int * int
        | ConstructorOwner of int * int
        | AnonymousOwner of string * (int * int)
        | ModuleOwner of string list

    type private Owner =
        { Name: string
          Line: int
          Key: OwnerKey
          TypeKey: (int * int) option
          Self: string option
          Path: string list }

    type private ValueExpr =
        | ValuePath of string list
        | ValueInvoke of ValueExpr * ValueExpr list
        | ValueBody of OwnerKey * deferred: bool
        | ValueJoin of ValueExpr list
        | ValueTuple of ValueExpr list
        | ValueProjection of int * ValueExpr
        | ValueFresh
        | ValueOther

    type private ValueReference =
        { OwnerKey: OwnerKey
          Expression: ValueExpr
          FileName: string
          Position: int * int }

    type private Invocation =
        { Target: OwnerKey
          Applied: int
          Context: Map<OwnerKey * string, ValueReference> }

    type private ValueSummary =
        { Callables: Set<Invocation>
          Deferred: Set<Invocation>
          Elements: ValueSummary list
          Complete: bool }

    type private CallArgument =
        | ArgumentPath of string list
        | ArgumentCallable of Owner
        | ArgumentValue of ValueExpr
        | ArgumentReference of ValueReference
        | ArgumentOther

    type private StorageSlot =
        | FieldSlot of string
        | ElementSlot of int
        | AnyElementSlot
        | OtherFieldSlot

    type private StorageExpr =
        | DirectStorage of string list
        | CallableStorage of ValueExpr
        | ShallowCopyStorage of string list
        | CallResultStorage of string list * StorageExpr list
        | AllocatedStorage of copied: StorageExpr option * (StorageSlot * StorageExpr) list
        | ElementStorage of StorageExpr * StorageSlot
        | UnknownStorage of string
        | FreshStorage
        | JoinedStorage of StorageExpr list

    type private StorageWrite =
        { Receiver: StorageExpr
          Slot: StorageSlot
          Value: StorageExpr
          Append: bool }

    type private Access =
        | Read of string list
        | Write of string list * readsExisting: bool * storageTarget: string list
        | Assign of string list
        | MutatingCall of string list
        | MutatingValue of StorageExpr
        | Call of string list * CallArgument list
        | CallValue of ValueExpr * CallArgument list
        | Force of ValueExpr
        | PatternUse of string
        | UnsupportedOperation of string
        | Ambient of FlowDirection * string * string list

    type private Fact =
        | Callable of Owner
        | Parent of OwnerKey * OwnerKey
        | Cell of OwnerKey * string
        | OpaqueCallable of OwnerKey
        | Parameter of OwnerKey * string * bool * int
        | ValueType of OwnerKey * string * string list
        | DeclaredType of (int * int) * string list * Set<string> option
        | PropertyOwner of OwnerKey
        | Arity of OwnerKey * int
        | SequenceParameter of OwnerKey * string
        | Bound of OwnerKey * string * range
        | BindingValue of OwnerKey * string * ValueExpr * range * range
        | Alias of OwnerKey * string * string list * range
        | Storage of OwnerKey * string * StorageExpr * range
        | Rebinding of OwnerKey * string * StorageExpr * ValueExpr * range * range option
        | StoredValue of Owner * StorageWrite * range * range option
        | ConditionalCall of OwnerKey * range
        | ReturnValue of OwnerKey * ValueExpr * StorageExpr option * range
        | TypeData of (int * int) * string * bool
        | TypeProperty of (int * int) * string * bool
        | TypeMethod of (int * int) * string
        | OpenDispatch of (int * int)
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
        | Captured of OwnerKey * string * bool
        | Unknown of string

    type private StorageOrigin =
        | ParameterOrigin of string
        | CaptureOrigin of OwnerKey * string
        | SharedOrigin of FlowSource * string
        | FreshOrigin
        | UnknownOrigin of string

    type private StorageSummary =
        { Outer: Set<StorageOrigin>
          Reachable: Set<StorageOrigin>
          Complete: bool }

    type private AllocationKey = OwnerKey * (int * int) * (int * int) list * int list

    type private HeapValue =
        { Origins: Set<StorageOrigin>
          Locations: Set<AllocationKey>
          Callables: Set<Invocation>
          Complete: bool }

    type private StorageState =
        { Bindings: Map<OwnerKey * string, HeapValue>
          Heap: Map<AllocationKey, Map<StorageSlot, HeapValue>>
          Calls: Map<OwnerKey * (int * int), HeapValue>
          Complete: bool }

    type private StorageEvent =
        | BindStorage of string * StorageExpr
        | RebindStorage of string * StorageExpr
        | StoreStorage of StorageWrite
        | InvokeStorage of ValueExpr * CallArgument list

    type private UnknownReason = UnknownCall of string

    type private SummaryEffect =
        | KnownEffect of FlowDirection * FlowSource * string
        | ParameterWrite of string
        | ParameterCall of string * CallArgument list
        | CaptureRead of OwnerKey * string
        | CaptureWrite of OwnerKey * string
        | CaptureCall of OwnerKey * string * CallArgument list
        | UnknownEffect of UnknownReason

    type private Resolvers =
        { Resolve: Owner -> string list -> range -> Resolution option
          Storage: Owner -> string list -> range -> StorageSummary
          StorageValue: Owner -> StorageExpr -> range -> StorageSummary
          Parameters: Map<OwnerKey, (string * bool) list>
          ArgumentIndex: Map<OwnerKey * string, int>
          Arity: Map<OwnerKey, int>
          ReturnValues: Map<OwnerKey, ValueExpr * range>
          PureConstructors: Set<string list * string>
          Values: Map<OwnerKey * string, ValueReference> -> Owner -> ValueExpr -> range -> ValueSummary
          OpaqueRead: Owner -> string list -> range -> bool
          Owners: Map<OwnerKey, Owner> }

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

    let rec private typeNames annotation =
        match annotation with
        | SynType.LongIdent(SynLongIdent(id = identifiers)) -> names identifiers
        | SynType.App(typeName = inner)
        | SynType.Paren(innerType = inner) -> typeNames inner
        | SynType.Array _ -> [ "array" ]
        | _ -> []

    let rec private patternTypes ownerKey pattern =
        match pattern with
        | SynPat.Typed(pat = inner; targetType = annotation) ->
            patternNames false inner
            |> List.map (fun (name, _) -> ValueType(ownerKey, name, typeNames annotation))
        | SynPat.LongIdent(argPats = SynArgPats.Pats arguments)
        | SynPat.Tuple(elementPats = arguments) -> List.collect (patternTypes ownerKey) arguments
        | SynPat.Paren(pat = inner)
        | SynPat.Attrib(pat = inner) -> patternTypes ownerKey inner
        | _ -> []

    let private propertyBinding (SynBinding(valData = SynValData(memberFlags = flags); headPat = pattern)) =
        match flags with
        | Some flags ->
            match flags.MemberKind, pattern with
            | (SynMemberKind.PropertyGet | SynMemberKind.PropertySet | SynMemberKind.PropertyGetSet), _ -> true
            | SynMemberKind.Member, SynPat.LongIdent(argPats = SynArgPats.Pats []) -> true
            | _ -> false
        | None -> false

    let private isFunctionBinding (SynBinding(headPat = pattern; expr = body; isMutable = mutableBinding)) =
        match pattern, body, mutableBinding with
        | SynPat.LongIdent(argPats = SynArgPats.Pats(_ :: _)), _, _
        | _, SynExpr.Lambda _, false
        | _, SynExpr.MatchLambda _, false
        | _, SynExpr.Typed(expr = SynExpr.Lambda _ | SynExpr.MatchLambda _), false -> true
        | _ -> false

    let private ownsFlows binding parent =
        match parent with
        | SyntaxNode.SynMemberDefn(SynMemberDefn.Member _ | SynMemberDefn.GetSetMember _) -> true
        | SyntaxNode.SynModule(SynModuleDecl.Let _)
        | SyntaxNode.SynMemberDefn(SynMemberDefn.LetBindings _) -> isFunctionBinding binding
        | SyntaxNode.SynExpr(SynExpr.LetOrUse _) -> isFunctionBinding binding
        | SyntaxNode.SynExpr(SynExpr.ObjExpr _) -> true
        | _ -> false

    let private typeKeyOf (ancestors: SyntaxNode list) =
        ancestors
        |> List.tryPick (fun node ->
            match node with
            | SyntaxNode.SynTypeDefn(SynTypeDefn(range = typeRange)) -> Some(key typeRange)
            | _ -> None)

    let private bindingKey (SynBinding(headPat = pattern)) = FunctionOwner(key pattern.Range)

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

    let private anonymousOwner kind expressionRange ancestors =
        { Key = AnonymousOwner(kind, key expressionRange)
          Name = "<" + kind + ">"
          Line = expressionRange.StartLine
          TypeKey = typeKeyOf ancestors
          Self = None
          Path = modulePathOf ancestors }

    let private coldOwner expression ancestors =
        match expression with
        | SynExpr.App(funcExpr = SynExpr.Ident builder; argExpr = SynExpr.ComputationExpr(range = bodyRange)) when
            builder.idText = "seq" || builder.idText = "async"
            ->
            Some(anonymousOwner builder.idText bodyRange ancestors)
        | SynExpr.Lazy(range = bodyRange) -> Some(anonymousOwner "lazy" bodyRange ancestors)
        | _ -> None

    let private moduleOwner ancestors =
        let path = modulePathOf ancestors

        { Key = ModuleOwner path
          Name = "<module>"
          Line = 0
          TypeKey = None
          Self = None
          Path = path }

    let private constructorOwner ancestors =
        ancestors
        |> List.tryPick (function
            | SyntaxNode.SynTypeDefn(SynTypeDefn(typeInfo = SynComponentInfo(longId = identifiers); range = typeRange)) ->
                identifiers
                |> List.tryLast
                |> Option.map (fun identifier ->
                    { Key = ConstructorOwner(key typeRange)
                      Name = identifier.idText
                      Line = typeRange.StartLine
                      TypeKey = Some(key typeRange)
                      Self = None
                      Path = modulePathOf ancestors })
            | _ -> None)

    let private ownerOf (nodes: SyntaxNode list) =
        let rec search nodes =
            match nodes with
            | SyntaxNode.SynExpr(SynExpr.ComputationExpr _) :: SyntaxNode.SynExpr expression :: ancestors ->
                match coldOwner expression ancestors with
                | Some current -> Some current
                | None -> search ancestors
            | SyntaxNode.SynExpr(SynExpr.Lazy _ as expression) :: ancestors -> coldOwner expression ancestors
            | SyntaxNode.SynExpr(SynExpr.Lambda(range = lambdaRange)) :: ancestors ->
                Some
                    { Key = AnonymousOwner("lambda", key lambdaRange)
                      Name = "<lambda>"
                      Line = lambdaRange.StartLine
                      TypeKey = typeKeyOf ancestors
                      Self = None
                      Path = modulePathOf ancestors }
            | SyntaxNode.SynBinding binding :: (parent :: _ as ancestors) when ownsFlows binding parent ->
                Some(owner binding ancestors)
            | SyntaxNode.SynMemberDefn(SynMemberDefn.LetBindings(isStatic = false) | SynMemberDefn.AutoProperty(
                isStatic = false)) :: ancestors -> constructorOwner ancestors
            | SyntaxNode.SynMemberDefn(SynMemberDefn.LetBindings(isStatic = true) | SynMemberDefn.AutoProperty(
                isStatic = true)) :: ancestors -> Some(moduleOwner nodes)
            | _ :: ancestors -> search ancestors
            | [] -> Some(moduleOwner nodes)

        search nodes
        |> Option.map (fun current ->
            match current.Key with
            | ModuleOwner _ -> moduleOwner nodes
            | _ -> current)

    let rec private rootNames expression =
        match expression with
        | SynExpr.Ident identifier -> [ identifier.idText ]
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = identifiers)) -> names identifiers
        | SynExpr.DotGet(expr = inner; longDotId = SynLongIdent(id = identifiers)) ->
            rootNames inner @ names identifiers
        | SynExpr.App(funcExpr = inner)
        | SynExpr.TypeApp(expr = inner)
        | SynExpr.DotIndexedGet(objectExpr = inner)
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner) -> rootNames inner
        | SynExpr.Upcast(expr = inner)
        | SynExpr.Downcast(expr = inner) -> rootNames inner
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
            Some(Write(identifiers, expressionContains identifiers rhs, identifiers))
        | SynExpr.DotIndexedSet(objectExpr = target; valueExpr = rhs) ->
            let identifiers = rootNames target
            Some(Write(identifiers, expressionContains identifiers rhs, identifiers))
        | SynExpr.App(
            funcExpr = SynExpr.App(
                funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ operator ])); argExpr = target)
            argExpr = rhs) when operator.idText = "op_ColonEquals" ->
            let identifiers = rootNames target
            Some(Write(identifiers, expressionContains identifiers rhs, identifiers))
        | SynExpr.App(
            funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = [ arrayModule; operation ])); argExpr = target) when
            arrayModule.idText = "Array" && operation.idText = "set"
            ->
            Some(Write(rootNames target, false, rootNames target))
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
            | _ when identifiers.Length > 1 ->
                let path = names identifiers
                Some(Write(path, false, List.take (path.Length - 1) path))
            | _ -> Some(Assign(names identifiers))
        | SynExpr.DotSet(targetExpr = target; longDotId = SynLongIdent(id = identifiers)) ->
            Some(Write(rootNames target @ names identifiers, false, rootNames target))
        | SynExpr.App(funcExpr = SynExpr.Ident operation; argExpr = target) when
            operation.idText = "incr" || operation.idText = "decr"
            ->
            Some(Write(rootNames target, true, rootNames target))
        | _ -> writeAccess expression

    let rec private applicationParts expression =
        let rec parts expression =
            match expression with
            | SynExpr.App(funcExpr = func; argExpr = argument) ->
                let root, arguments = parts func
                root, arguments @ [ argument ]
            | SynExpr.TypeApp(expr = inner)
            | SynExpr.Paren(expr = inner)
            | SynExpr.Typed(expr = inner) -> parts inner
            | _ -> expression, []

        let func, arguments = parts expression

        match rootNames func, arguments with
        | [ "op_PipeRight" ], [ source; target ]
        | [ "op_PipeLeft" ], [ target; source ] ->
            let target, arguments = applicationParts target
            target, arguments @ [ source ]
        | _ -> func, arguments

    let private elementSlot expression =
        match expression with
        | SynExpr.Const(constant = SynConst.Int32 index) -> ElementSlot index
        | _ -> AnyElementSlot

    let rec private storageExpr expression =
        let value expression =
            storageExpr expression |> Option.defaultValue (UnknownStorage "storage value")

        match expression with
        | SynExpr.Const _ -> Some FreshStorage
        | SynExpr.Lambda(range = site) -> Some(CallableStorage(ValueBody(AnonymousOwner("lambda", key site), false)))
        | SynExpr.LetOrUse binding -> storageExpr binding.Body
        | SynExpr.Sequential(expr2 = result)
        | SynExpr.YieldOrReturn(expr = result) -> storageExpr result
        | SynExpr.Record(copyInfo = copied; recordFields = fields) ->
            let copied = copied |> Option.bind (fun (expression, _) -> storageExpr expression)

            let fields =
                fields
                |> List.choose
                    (fun (SynExprRecordField(fieldName = (SynLongIdent(id = identifiers), _); expr = expression)) ->
                        expression
                        |> Option.map (fun expression -> FieldSlot(List.last (names identifiers)), value expression))

            Some(AllocatedStorage(copied, fields))
        | SynExpr.Tuple(exprs = items)
        | SynExpr.ArrayOrList(exprs = items) ->
            Some(AllocatedStorage(None, items |> List.mapi (fun index item -> ElementSlot index, value item)))
        | SynExpr.DotIndexedGet(objectExpr = source; indexArgs = index) ->
            Some(ElementStorage(value source, elementSlot index))
        | SynExpr.DotGet(expr = source; longDotId = SynLongIdent(id = identifiers)) ->
            match storageExpr source with
            | Some(DirectStorage path) -> Some(DirectStorage(boundedPath (path @ names identifiers)))
            | Some storage ->
                Some(
                    names identifiers
                    |> List.fold (fun source memberName -> ElementStorage(source, FieldSlot memberName)) storage
                )
            | None -> None
        | SynExpr.Match(clauses = clauses) ->
            clauses
            |> List.map (fun (SynMatchClause(resultExpr = result)) ->
                storageExpr result |> Option.defaultValue (UnknownStorage "match result"))
            |> JoinedStorage
            |> Some
        | SynExpr.Upcast(expr = inner)
        | SynExpr.Downcast(expr = inner)
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner)
        | SynExpr.TypeApp(expr = inner) -> storageExpr inner
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
                [ thenExpr; elseExpr ]
                |> List.map (fun branch -> storageExpr branch |> Option.defaultValue (UnknownStorage "branch result"))
                |> JoinedStorage
                |> Some
            | None, None -> storageExpr thenExpr
        | SynExpr.App(argExpr = argument) when createsRef expression ->
            Some(AllocatedStorage(None, [ FieldSlot "Value", value argument ]))
        | SynExpr.App(argExpr = argument) when createsContainer expression ->
            Some(AllocatedStorage(None, [ AnyElementSlot, ElementStorage(value argument, AnyElementSlot) ]))
        | SynExpr.New(targetType = annotation; expr = argument) when isContainerType annotation ->
            Some(AllocatedStorage(None, [ AnyElementSlot, ElementStorage(value argument, AnyElementSlot) ]))
        | SynExpr.New(targetType = annotation; expr = argument) ->
            Some(CallResultStorage(typeNames annotation, [ value argument ]))
        | _ when createsContainer expression -> Some(AllocatedStorage(None, []))
        | SynExpr.App _ ->
            let func, arguments = applicationParts expression

            match rootNames func with
            | [] -> None
            | identifiers -> Some(CallResultStorage(identifiers, arguments |> List.map value))
        | _ when createsArray expression -> Some(AllocatedStorage(None, []))
        | _ ->
            match rootNames expression with
            | [] -> None
            | identifiers -> Some(DirectStorage identifiers)

    let private lambdaOwner (expression: SynExpr) =
        match expression with
        | SynExpr.Lambda(range = lambdaRange) ->
            Some
                { Key = AnonymousOwner("lambda", key lambdaRange)
                  Name = "<lambda>"
                  Line = lambdaRange.StartLine
                  TypeKey = None
                  Self = None
                  Path = [] }
        | SynExpr.Typed(expr = SynExpr.Lambda(range = lambdaRange))
        | SynExpr.Paren(expr = SynExpr.Lambda(range = lambdaRange)) ->
            Some
                { Key = AnonymousOwner("lambda", key lambdaRange)
                  Name = "<lambda>"
                  Line = lambdaRange.StartLine
                  TypeKey = None
                  Self = None
                  Path = [] }
        | _ -> None

    let rec private valueExpr expression =
        match coldOwner expression [], lambdaOwner expression with
        | Some owner, _ -> ValueBody(owner.Key, true)
        | _, Some owner -> ValueBody(owner.Key, false)
        | _ ->
            match expression with
            | SynExpr.LetOrUse binding -> valueExpr binding.Body
            | SynExpr.Sequential(expr2 = result)
            | SynExpr.YieldOrReturn(expr = result)
            | SynExpr.Paren(expr = result)
            | SynExpr.Typed(expr = result) -> valueExpr result
            | SynExpr.IfThenElse(ifExpr = condition; thenExpr = whenTrue; elseExpr = whenFalse) ->
                match literalBool condition, whenFalse with
                | Some true, _ -> valueExpr whenTrue
                | Some false, Some result -> valueExpr result
                | Some false, None -> ValueOther
                | None, _ -> ValueJoin(valueExpr whenTrue :: (whenFalse |> Option.map valueExpr |> Option.toList))
            | SynExpr.Tuple(exprs = items) -> ValueTuple(List.map valueExpr items)
            | SynExpr.Record _ -> ValueFresh
            | _ when createsContainer expression || createsArray expression || createsRef expression -> ValueFresh
            | SynExpr.App _ ->
                let func, arguments = applicationParts expression

                match rootNames func, arguments with
                | [ "fst" ], [ argument ] -> ValueProjection(0, valueExpr argument)
                | [ "snd" ], [ argument ] -> ValueProjection(1, valueExpr argument)
                | _ -> ValueInvoke(valueExpr func, List.map valueExpr arguments)
            | _ ->
                match rootNames expression with
                | [] -> ValueOther
                | identifiers -> ValuePath identifiers

    let private callArgument expression =
        match valueExpr expression with
        | ValuePath identifiers -> ArgumentPath identifiers
        | value -> ArgumentValue value

    let rec private resultRange expression =
        match expression with
        | SynExpr.LetOrUse binding -> resultRange binding.Body
        | SynExpr.Sequential(expr2 = result)
        | SynExpr.Paren(expr = result)
        | SynExpr.Typed(expr = result) -> resultRange result
        | _ -> expression.Range

    let private forcingCall identifiers arguments =
        match identifiers, arguments with
        | [ "Seq"; ("toList" | "toArray" | "sum" | "length" | "iter") ], arguments
        | [ "Async"; "RunSynchronously" ], arguments when not (List.isEmpty arguments) ->
            Some(valueExpr (List.last arguments))
        | [ root; "Force" ], _ -> Some(ValuePath [ root ])
        | _ -> None

    let private isNestedApplicationFunction (ancestors: SyntaxNode list) expressionRange =
        ancestors
        |> List.exists (fun node ->
            match node with
            | SyntaxNode.SynExpr(SynExpr.App(funcExpr = func)) -> Range.equals func.Range expressionRange
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
            | SyntaxNode.SynExpr(SynExpr.While(whileExpr = condition; doExpr = body)) ->
                literalBool condition <> Some false
                || not (Range.rangeContainsRange body.Range expressionRange)
            | _ -> true)

    let private accessRange expression =
        match expression with
        | SynExpr.LongIdentSet(longDotId = target) -> target.Range
        | _ ->
            writeTarget expression
            |> Option.map _.Range
            |> Option.defaultValue expression.Range

    let private joinScope ancestors =
        ancestors
        |> List.tryPick (function
            | SyntaxNode.SynExpr(SynExpr.For(doBody = body))
            | SyntaxNode.SynExpr(SynExpr.ForEach(bodyExpr = body))
            | SyntaxNode.SynExpr(SynExpr.While(doExpr = body)) -> Some body.Range
            | SyntaxNode.SynExpr(SynExpr.IfThenElse(ifExpr = condition; range = branchRange)) when
                literalBool condition = None
                ->
                Some branchRange
            | SyntaxNode.SynExpr(SynExpr.Match(range = matchRange)) -> Some matchRange
            | _ -> None)

    let private expressionFacts current ancestors (expression: SynExpr) =
        let stored target slot append value =
            let write =
                { Receiver = target
                  Slot = slot
                  Value = storageExpr value |> Option.defaultValue (UnknownStorage "stored value")
                  Append = append }

            StoredValue(current, write, expression.Range, joinScope ancestors)

        if not (isReachableExpression ancestors expression.Range) then
            []
        else
            match expression with
            | SynExpr.AddressOf _ -> [ Candidate(current, UnsupportedOperation "byref access", expression.Range) ]
            | SynExpr.New(targetType = annotation; expr = argument) ->
                [ Candidate(current, Call(typeNames annotation, [ callArgument argument ]), expression.Range) ]
            | SynExpr.DotIndexedGet(objectExpr = source) ->
                [ Candidate(current, Read(rootNames source @ [ "Item" ]), expression.Range) ]
            | SynExpr.DotGet(expr = source; longDotId = SynLongIdent(id = identifiers)) when
                not (mutatingMethods.Contains(List.last (names identifiers)))
                && names identifiers <> [ "Value" ]
                ->
                [ Candidate(current, Read(rootNames source @ names identifiers), expression.Range) ]
            | SynExpr.LongIdentSet(longDotId = SynLongIdent(id = identifiers); expr = value) when identifiers.Length > 1 ->
                let target = names identifiers

                [ stored (DirectStorage(List.take (target.Length - 1) target)) (FieldSlot(List.last target)) false value
                  Candidate(current, Write(target, false, List.take (target.Length - 1) target), expression.Range) ]
            | SynExpr.DotSet(targetExpr = target; longDotId = SynLongIdent(id = identifiers); rhsExpr = value) ->
                let targetPath = rootNames target @ names identifiers

                let receiver =
                    storageExpr target |> Option.defaultValue (UnknownStorage "member target")

                [ stored receiver (FieldSlot(List.last targetPath)) false value
                  Candidate(current, Write(targetPath, false, rootNames target), expression.Range)
                  Candidate(current, MutatingValue receiver, expression.Range) ]
            | SynExpr.DotIndexedSet(objectExpr = target; indexArgs = index; valueExpr = value) ->
                let targetPath = rootNames target

                [ stored
                      (storageExpr target |> Option.defaultValue (UnknownStorage "indexed target"))
                      (elementSlot index)
                      false
                      value
                  Candidate(
                      current,
                      Write(targetPath, expressionContains targetPath value, targetPath),
                      expression.Range
                  ) ]
            | SynExpr.LongIdentSet(longDotId = SynLongIdent(id = [ identifier ]); expr = value) ->
                [ Rebinding(
                      current.Key,
                      identifier.idText,
                      storageExpr value |> Option.defaultValue (UnknownStorage "assigned value"),
                      valueExpr value,
                      expression.Range,
                      joinScope ancestors
                  )
                  Candidate(current, Assign [ identifier.idText ], identifier.idRange) ]
            | SynExpr.For(ident = identifier; doBody = body) -> [ Bound(current.Key, identifier.idText, body.Range) ]
            | SynExpr.ForEach(enumExpr = source; bodyExpr = body; pat = pattern) ->
                Candidate(current, Force(valueExpr source), source.Range)
                :: (patternNames false pattern
                    |> List.map (fun (name, _) -> Bound(current.Key, name, body.Range)))
            | SynExpr.DotGet(expr = source; longDotId = SynLongIdent(id = [ operation ])) when
                operation.idText = "Value"
                ->
                [ Candidate(current, Force(valueExpr source), expression.Range) ]
            | SynExpr.DotGet(expr = source; longDotId = SynLongIdent(id = [ operation ])) when
                mutatingMethods.Contains operation.idText
                ->
                match source with
                | SynExpr.App _
                | SynExpr.DotIndexedGet _
                | SynExpr.Paren(expr = SynExpr.App _) ->
                    storageExpr source
                    |> Option.map (fun storage -> Candidate(current, MutatingValue storage, expression.Range))
                    |> Option.toList
                | _ -> [ Candidate(current, MutatingCall(rootNames source), expression.Range) ]
            | SynExpr.App _ when not (isNestedApplicationFunction ancestors expression.Range) ->
                let access =
                    expressionAccess ancestors expression
                    |> Option.toList
                    |> List.map (fun access -> Candidate(current, access, accessRange expression))

                let call =
                    let func, arguments = applicationParts expression

                    match rootNames func with
                    | _ when isWriteTarget ancestors func.Range -> []
                    | [] ->
                        [ Candidate(current, CallValue(valueExpr func, arguments |> List.map callArgument), func.Range) ]
                    | identifiers ->
                        match forcingCall identifiers arguments with
                        | Some source -> [ Candidate(current, Force source, func.Range) ]
                        | None ->
                            [ Candidate(current, Call(identifiers, arguments |> List.map callArgument), func.Range) ]

                let stored =
                    let func, arguments = applicationParts expression
                    let identifiers = rootNames func

                    match List.tryLast identifiers with
                    | Some("Add" | "AddRange" | "Insert" | "Enqueue" | "Push" | "AddFirst" | "AddLast") when
                        identifiers.Length > 1
                        ->
                        let target = List.take (identifiers.Length - 1) identifiers

                        let arguments =
                            if List.tryLast identifiers = Some "Insert" then
                                arguments
                                |> List.collect (function
                                    | SynExpr.Tuple(exprs = items) -> items |> List.tryLast |> Option.toList
                                    | value -> [ value ])
                            else
                                arguments

                        arguments
                        |> List.map (fun argument ->
                            let write = stored (DirectStorage target) AnyElementSlot true argument

                            match List.tryLast identifiers, write with
                            | Some "AddRange", StoredValue(owner, write, site, scope) ->
                                StoredValue(
                                    owner,
                                    { write with
                                        Value = ElementStorage(write.Value, AnyElementSlot) },
                                    site,
                                    scope
                                )
                            | _ -> write)
                    | Some "op_ColonEquals" when arguments.Length = 2 ->
                        [ stored (DirectStorage(rootNames arguments[0])) (FieldSlot "Value") false arguments[1] ]
                    | _ -> []

                let conditional =
                    if (joinScope ancestors).IsSome then
                        call
                        |> List.choose (function
                            | Candidate(_, (Call _ | CallValue _), site) -> Some(ConditionalCall(current.Key, site))
                            | _ -> None)
                    else
                        []

                access @ call @ stored @ conditional
            | _ ->
                expressionAccess ancestors expression
                |> Option.map (fun access -> Candidate(current, access, accessRange expression))
                |> Option.toList

    /// Where a local pattern's names are visible; None for the owner's own parameters, which are already facts.
    let private patternScope current (pattern: SynPat) (ancestors: SyntaxNode list) =
        let lambdaParameter =
            ancestors
            |> List.exists (function
                | SyntaxNode.SynExpr(SynExpr.Lambda(parsedData = Some(parameters, _); range = lambdaRange)) when
                    current.Key = AnonymousOwner("lambda", key lambdaRange)
                    ->
                    parameters
                    |> List.exists (fun parameter -> Range.rangeContainsRange parameter.Range pattern.Range)
                | _ -> false)

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

        if lambdaParameter then None else search ancestors

    let private patternFacts current ancestors pattern =
        match pattern, patternScope current pattern ancestors with
        | SynPat.Named(SynIdent(identifier, _), _, _, _), Some scope
        | SynPat.OptionalVal(identifier, _), Some scope
        | SynPat.LongIdent(longDotId = SynLongIdent(id = [ identifier ]); argPats = SynArgPats.Pats []), Some scope ->
            [ Bound(current.Key, identifier.idText, scope) ]
        | SynPat.LongIdent(longDotId = SynLongIdent(id = [ identifier ])), scope ->
            match ancestors with
            | SyntaxNode.SynBinding(SynBinding(headPat = head)) :: _ when Range.equals head.Range pattern.Range ->
                scope
                |> Option.map (fun scope -> Bound(current.Key, identifier.idText, scope))
                |> Option.toList
            | _ -> [ Candidate(current, PatternUse identifier.idText, identifier.idRange) ]
        | _ -> []

    let private parameterFacts current (SynBinding(headPat = pattern)) =
        match pattern with
        | SynPat.LongIdent(argPats = SynArgPats.Pats arguments) ->
            arguments
            |> List.collect (patternNames false)
            |> List.mapi (fun index (name, container) -> Parameter(current.Key, name, container, index))
        | _ -> []

    let private sequenceParameterFacts current (SynBinding(headPat = pattern)) =
        let rec sequenceType annotation =
            match annotation with
            | SynType.Array _ -> true
            | SynType.App(typeName = inner) -> sequenceType inner
            | SynType.LongIdent(SynLongIdent(id = identifiers)) ->
                identifiers
                |> List.tryLast
                |> Option.exists (fun identifier ->
                    Set.contains identifier.idText (set [ "array"; "list"; "ResizeArray"; "List" ]))
            | _ -> false

        let rec parameters pattern =
            match pattern with
            | SynPat.Typed(pat = pattern; targetType = annotation) when sequenceType annotation ->
                patternNames false pattern
                |> List.map (fun (name, _) -> SequenceParameter(current.Key, name))
            | SynPat.LongIdent(argPats = SynArgPats.Pats arguments)
            | SynPat.Tuple(elementPats = arguments) -> List.collect parameters arguments
            | SynPat.Paren(pat = pattern) -> parameters pattern
            | _ -> []

        parameters pattern

    let private lambdaParameters current (SynSimplePats.SimplePats(pats = parameters)) =
        let rec names container parameter =
            match parameter with
            | SynSimplePat.Id(ident = identifier; isCompilerGenerated = false) -> [ identifier.idText, container ]
            | SynSimplePat.Typed(pat = parameter; targetType = annotation) ->
                names (isContainerType annotation) parameter
            | SynSimplePat.Attrib(pat = parameter) -> names container parameter
            | _ -> []

        parameters
        |> List.collect (names false)
        |> List.mapi (fun index (name, container) -> Parameter(current.Key, name, container, index))

    let private bindingValues current pattern (expression: SynExpr) scope =
        let rec bindings projection pattern =
            match pattern with
            | SynPat.Tuple(elementPats = items) ->
                items
                |> List.mapi (fun index item -> bindings (ValueProjection(index, projection)) item)
                |> List.concat
            | SynPat.Paren(pat = pattern)
            | SynPat.Typed(pat = pattern) -> bindings projection pattern
            | _ ->
                patternNames false pattern
                |> List.map (fun (name, _) -> BindingValue(current.Key, name, projection, scope, expression.Range))

        bindings (valueExpr expression) pattern

    let private dataFacts
        (SynBinding(headPat = pattern; isMutable = isMutable; expr = body; accessibility = access))
        parent
        ancestors
        =
        let container = createsContainer body

        let exported =
            match access, pattern with
            | Some(SynAccess.Private _), _
            | _, SynPat.Named(accessibility = Some(SynAccess.Private _)) -> false
            | _ -> true

        let tracked =
            (container && (exported || not (createsArray body)))
            || isMutable
            || createsRef body

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

            let opaque =
                match binding with
                | SynBinding(trivia = { LeadingKeyword = FSharp.Compiler.SyntaxTrivia.SynLeadingKeyword.Extern _ }) ->
                    [ OpaqueCallable current.Key ]
                | _ -> []

            let returnedValue =
                match binding with
                | SynBinding(expr = body) ->
                    [ ReturnValue(current.Key, valueExpr body, storageExpr body, resultRange body) ]

            let arity =
                match binding with
                | SynBinding(headPat = SynPat.LongIdent(argPats = SynArgPats.Pats arguments)) -> arguments.Length
                | _ -> 0

            let parent =
                ownerOf path
                |> Option.map (fun parent -> Parent(current.Key, parent.Key))
                |> Option.toList

            let property =
                if propertyBinding binding then
                    [ PropertyOwner current.Key ]
                else
                    []

            let annotated =
                match binding with
                | SynBinding(headPat = pattern) -> patternTypes current.Key pattern

            (Callable current :: Arity(current.Key, arity) :: parameters)
            @ annotated
            @ property
            @ sequenceParameterFacts current binding
            @ parent
            @ opaque
            @ returnedValue
        | SyntaxNode.SynBinding binding, (SyntaxNode.SynModule(SynModuleDecl.Let _) as parent) :: _, _ when
            not (isFunctionBinding binding)
            ->
            dataFacts binding parent path
        | SyntaxNode.SynBinding binding, (SyntaxNode.SynMemberDefn(SynMemberDefn.LetBindings _) as parent) :: _, _ when
            not (isFunctionBinding binding)
            ->
            dataFacts binding parent path
        | SyntaxNode.SynBinding binding, parent :: _, None -> dataFacts binding parent path
        | SyntaxNode.SynTypeDefn(SynTypeDefn(
            typeInfo = SynComponentInfo(longId = identifiers); typeRepr = representation; range = typeRange)),
          _,
          _ ->
            let constructors =
                match representation with
                | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.Union(unionCases = cases)) ->
                    cases
                    |> List.map (fun (SynUnionCase(ident = SynIdent(identifier, _))) -> identifier.idText)
                | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.Record _) ->
                    identifiers |> List.tryLast |> Option.map _.idText |> Option.toList
                | _ -> []

            let fields =
                match representation with
                | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.Record(recordFields = fields)) ->
                    fields
                    |> List.choose (fun (SynField(idOpt = name)) -> name |> Option.map _.idText)
                    |> Set.ofList
                    |> Some
                | _ -> None

            DeclaredType(key typeRange, modulePathOf path @ names identifiers, fields)
            :: (constructors |> List.map (fun name -> PureConstructor(modulePathOf path, name)))
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

            let boundFacts =
                patternNames false pattern
                |> List.map (fun (name, _) -> Bound(current.Key, name, scope))

            let cells =
                match binding with
                | SynBinding(isMutable = true) ->
                    patternNames false pattern
                    |> List.map (fun (name, _) -> Cell(current.Key, name))
                | _ -> []

            boundFacts
            @ cells
            @ aliasFacts
            @ storageFacts
            @ patternTypes current.Key pattern
            @ bindingValues current pattern expression scope
        | SyntaxNode.SynExpr(SynExpr.Lambda(args = parameters; parsedData = parsed; body = body)), _, Some current ->
            let parent =
                ownerOf path
                |> Option.map (fun parent -> Parent(current.Key, parent.Key))
                |> Option.toList

            let parameters, arity =
                match parsed with
                | Some(patterns, _) ->
                    patterns
                    |> List.collect (patternNames false)
                    |> List.mapi (fun index (name, container) -> Parameter(current.Key, name, container, index)),
                    patterns.Length
                | None -> let facts = lambdaParameters current parameters in facts, facts.Length

            Callable current
            :: Arity(current.Key, arity)
            :: ReturnValue(current.Key, valueExpr body, storageExpr body, resultRange body)
            :: (parameters @ parent)
        | SyntaxNode.SynExpr(SynExpr.ComputationExpr _ | SynExpr.Lazy _), _, Some current when
            (match current.Key with
             | AnonymousOwner(("seq" | "async" | "lazy"), _) -> true
             | _ -> false)
            ->
            let parent =
                ownerOf path
                |> Option.map (fun parent -> Parent(current.Key, parent.Key))
                |> Option.toList

            Callable current :: parent
        | SyntaxNode.SynModule(SynModuleDecl.Open(
            target = SynOpenDeclTarget.ModuleOrNamespace(longId = SynLongIdent(id = identifiers)))),
          _,
          _ -> [ Open(modulePathOf path, names identifiers, key node.Range) ]
        | SyntaxNode.SynMemberDefn(SynMemberDefn.ImplicitCtor(ctorArgs = arguments)), _, _ ->
            match typeKeyOf path with
            | Some typeKey ->
                let data =
                    patternNames false arguments
                    |> List.map (fun (name, container) -> TypeData(typeKey, name, container))

                match constructorOwner path with
                | Some current ->
                    [ Callable current
                      Arity(current.Key, 1)
                      Parent(current.Key, (moduleOwner path).Key)
                      ReturnValue(current.Key, ValueFresh, Some FreshStorage, node.Range) ]
                    @ (patternNames false arguments
                       |> List.mapi (fun index (name, container) -> Parameter(current.Key, name, container, index)))
                    @ patternTypes current.Key arguments
                    @ data
                | None -> data
            | None -> []
        | SyntaxNode.SynMemberDefn(SynMemberDefn.AbstractSlot _), _, _ ->
            typeKeyOf path |> Option.map OpenDispatch |> Option.toList
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

    let private callbackAccess identifiers arguments =
        let call callback arguments =
            match callback with
            | ArgumentPath path -> Call(path, arguments)
            | ArgumentValue value -> CallValue(value, arguments)
            | ArgumentReference reference -> CallValue(reference.Expression, arguments)
            | ArgumentCallable owner -> CallValue(ValueBody(owner.Key, false), arguments)
            | ArgumentOther -> CallValue(ValueOther, arguments)

        match identifiers, arguments with
        | [ ("List" | "Array"); ("map" | "iter" | "exists") ], callback :: source :: _ -> Some(call callback [ source ])
        | [ ("List" | "Array"); "fold" ], callback :: state :: source :: _ -> Some(call callback [ state; source ])
        | receiver, [ callback ] when List.tryLast receiver = Some "Sort" ->
            let source = ArgumentPath(List.take (receiver.Length - 1) receiver)
            Some(call callback [ source; source ])
        | _ -> None

    let private resolver (facts: Fact list) =
        let parents =
            facts
            |> List.choose (function
                | Parent(child, parent) when child <> parent -> Some(child, parent)
                | _ -> None)
            |> Map.ofList

        let rec lexicalScopes ownerKey =
            ownerKey
            :: (parents
                |> Map.tryFind ownerKey
                |> Option.map lexicalScopes
                |> Option.defaultValue [])

        let cells =
            facts
            |> List.choose (function
                | Cell(ownerKey, name) -> Some(ownerKey, name)
                | _ -> None)
            |> Set.ofList

        let opaqueCallables =
            facts
            |> List.choose (function
                | OpaqueCallable ownerKey -> Some ownerKey
                | _ -> None)
            |> Set.ofList

        let owners =
            facts
            |> List.choose (function
                | Callable owner
                | Candidate(owner, _, _) -> Some(owner.Key, owner)
                | _ -> None)
            |> Map.ofList

        let callableByName =
            facts
            |> List.choose (function
                | Callable owner when
                    (match Map.tryFind owner.Key parents with
                     | None
                     | Some(ModuleOwner _) -> true
                     | _ -> false)
                    ->
                    Some((owner.Path, owner.Name), owner)
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (name, entries) -> name, entries |> List.map snd |> List.sortBy (fun owner -> owner.Line))
            |> Map.ofList

        let lexicalCallables =
            owners
            |> Map.toList
            |> List.collect (fun (_, candidate) ->
                [ yield (candidate.Key, candidate.Name), candidate
                  match Map.tryFind candidate.Key parents with
                  | Some parent -> yield (parent, candidate.Name), candidate
                  | None -> () ])
            |> List.groupBy fst
            |> List.map (fun (name, entries) -> name, entries |> List.map snd |> List.sortByDescending _.Line)
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

        let arity =
            facts
            |> List.choose (function
                | Arity(ownerKey, count) -> Some(ownerKey, count)
                | _ -> None)
            |> Map.ofList

        let parameterContainers =
            parameters
            |> Map.toList
            |> List.collect (fun (ownerKey, items) ->
                items |> List.map (fun (name, container) -> (ownerKey, name), container))
            |> Map.ofList

        let valueTypes =
            facts
            |> List.choose (function
                | ValueType(ownerKey, name, annotation) -> Some((ownerKey, name), annotation)
                | _ -> None)
            |> Map.ofList

        let declaredTypes =
            facts
            |> List.choose (function
                | DeclaredType(typeKey, name, fields) -> Some(typeKey, (name, fields))
                | _ -> None)
            |> Map.ofList

        let propertyOwners =
            facts
            |> List.choose (function
                | PropertyOwner ownerKey -> Some ownerKey
                | _ -> None)
            |> Set.ofList

        let aliases =
            facts
            |> List.choose (function
                | Alias(ownerKey, name, source, scope) -> Some((ownerKey, name), (source, scope))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, entries) -> binding, List.map snd entries)
            |> Map.ofList

        let returnValues =
            facts
            |> List.choose (function
                | ReturnValue(ownerKey, value, _, valueRange) -> Some(ownerKey, (value, valueRange))
                | _ -> None)
            |> Map.ofList

        let rebindings =
            facts
            |> List.choose (function
                | Rebinding(ownerKey, name, storage, value, assignmentRange, loop) ->
                    Some((ownerKey, name), (storage, value, assignmentRange, loop))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (binding, entries) ->
                binding,
                List.map snd entries
                |> List.sortBy (fun (_, _, assignmentRange, _) -> key assignmentRange))
            |> Map.ofList

        let bindingValues =
            facts
            |> List.choose (function
                | BindingValue(ownerKey, name, value, scope, valueRange) ->
                    Some((ownerKey, name), (value, scope, valueRange))
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

        let openDispatch =
            facts
            |> List.choose (function
                | OpenDispatch typeKey -> Some typeKey
                | _ -> None)
            |> Set.ofList

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
                | Candidate(current, Write(identifiers, _, _), accessRange) ->
                    moduleValue moduleData opens current identifiers accessRange
                    |> Option.bind (fun (_, (_, container, position)) -> if container then Some position else None)
                | _ -> None)
            |> Set.ofList

        let isLocal current name accessRange =
            bound
            |> Map.tryFind (current.Key, name)
            |> Option.exists (List.exists (fun scope -> Range.rangeContainsRange scope accessRange))

        let bindingOwner current name accessRange =
            lexicalScopes current.Key
            |> List.choose (fun ownerKey -> owners |> Map.tryFind ownerKey)
            |> List.tryFind (fun scope ->
                isLocal scope name accessRange
                || Map.containsKey (scope.Key, name) parameterContainers)

        let writtenCaptures =
            facts
            |> List.choose (function
                | Candidate(current, (Write(root :: _, _, _) | Assign(root :: _) | MutatingCall(root :: _)), accessRange) ->
                    bindingOwner current root accessRange
                    |> Option.bind (fun scope ->
                        if scope.Key <> current.Key then
                            Some(scope.Key, root)
                        else
                            None)
                | _ -> None)
            |> Set.ofList

        let callable current identifiers (accessRange: range) =
            match identifiers with
            | [ name ] ->
                let lexical =
                    lexicalScopes current.Key
                    |> List.tryPick (fun scope ->
                        lexicalCallables |> Map.tryFind (scope, name) |> Option.bind List.tryHead)

                lexical
                |> Option.orElseWith (fun () ->
                    enclosing current.Path
                    |> List.tryPick (fun path ->
                        callableByName |> Map.tryFind (path, name) |> Option.bind List.tryLast))
                |> Option.orElseWith (fun () ->
                    opens
                    |> List.filter (fun (path, _, position) ->
                        position <= key accessRange && List.contains path (enclosing current.Path))
                    |> List.sortByDescending (fun (_, _, position) -> position)
                    |> List.tryPick (fun (_, target, _) ->
                        enclosing current.Path
                        |> List.tryPick (fun prefix ->
                            callableByName
                            |> Map.tryFind (prefix @ target, name)
                            |> Option.bind List.tryHead)))
            | [ self; name ] when current.Self = Some self ->
                owners
                |> Map.toList
                |> List.map snd
                |> List.tryFind (fun candidate -> candidate.TypeKey = current.TypeKey && candidate.Name = name)
            | _ :: _ ->
                let path, name =
                    identifiers |> List.take (identifiers.Length - 1), List.last identifiers

                enclosing current.Path
                |> List.tryPick (fun prefix ->
                    callableByName |> Map.tryFind (prefix @ path, name) |> Option.bind List.tryHead)
            | _ -> None

        let rec resolve visited current identifiers accessRange =
            let alias =
                match identifiers with
                | root :: _ when isLocal current root accessRange -> None
                | root :: tail when not (Set.contains root visited) ->
                    Map.tryFind (current.Key, root) aliases
                    |> Option.bind (List.tryFind (fun (_, scope) -> Range.rangeContainsRange scope accessRange))
                    |> Option.map (fun (source, _) -> source @ tail, Set.add root visited)
                | _ -> None

            let lexicalCallable = callable current identifiers accessRange

            let captured =
                match identifiers with
                | root :: _ ->
                    bindingOwner current root accessRange
                    |> Option.bind (fun scope ->
                        if scope.Key = current.Key then
                            None
                        else
                            let mutableCapture =
                                Map.tryFind (scope.Key, root) parameterContainers = Some true
                                || Set.contains (scope.Key, root) cells
                                || Set.contains (scope.Key, root) writtenCaptures

                            Some(Captured(scope.Key, root, mutableCapture)))
                | _ -> None

            match alias, identifiers with
            | Some(source, followed), _ -> resolve followed current source accessRange
            | None, [ root ] when
                lexicalCallable
                |> Option.exists (fun candidate ->
                    Map.tryFind candidate.Key parents = Some current.Key
                    || (bindingOwner current root accessRange).IsNone)
                ->
                lexicalCallable
                |> Option.map (fun target ->
                    if Set.contains target.Key opaqueCallables then
                        Unknown target.Name
                    else
                        CallableTarget target)
            | None, root :: _ when isLocal current root accessRange -> Some Local
            | None, _ when captured.IsSome -> captured
            | None, self :: memberName :: _ when current.Self = Some self ->
                match current.TypeKey with
                | Some typeKey when Set.contains typeKey openDispatch -> Unknown(String.concat "." identifiers)
                | Some typeKey when methods.Contains(typeKey, memberName) ->
                    lexicalCallable |> Option.map CallableTarget |> Option.defaultValue Local
                | typeKey ->
                    let container =
                        typeKey
                        |> Option.bind (fun typeKey -> properties |> Map.tryFind (typeKey, memberName))

                    Data(TypeState, memberName, container = Some true)
                |> Some
            | None, root :: _ ->
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
                    | Some target when Set.contains target.Key opaqueCallables -> Unknown(String.concat "." identifiers)
                    | Some target -> CallableTarget target
                    | None -> Unknown(String.concat "." identifiers)
                |> Some
            | None, [] -> None

        let opaqueRead current identifiers accessRange =
            let declared annotation =
                enclosing current.Path
                |> List.tryPick (fun prefix ->
                    declaredTypes
                    |> Map.toList
                    |> List.tryPick (fun (_, (name, fields)) ->
                        if name = prefix @ annotation then Some fields else None))

            let unsafeField ownerKey root memberName =
                match Map.tryFind (ownerKey, root) valueTypes with
                | Some annotation ->
                    match declared annotation with
                    | Some(Some fields) -> not (Set.contains memberName fields)
                    | Some None -> true
                    | None ->
                        let builtin =
                            annotation
                            |> List.tryLast
                            |> Option.exists (fun name ->
                                containerTypes.Contains name
                                || Set.contains name (set [ "array"; "list"; "ref"; "string" ]))

                        not (
                            builtin
                            && Set.contains memberName (set [ "Item"; "Count"; "Length"; "Value"; "IsEmpty" ])
                        )
                | None -> false

            match identifiers with
            | root :: memberName :: _ ->
                match resolve Set.empty current [ root ] accessRange with
                | Some(Argument(name, _)) -> unsafeField current.Key name memberName
                | Some(Captured(ownerKey, name, _)) -> unsafeField ownerKey name memberName
                | Some Local -> unsafeField current.Key root memberName
                | _ ->
                    let qualifier = List.take (identifiers.Length - 1) identifiers
                    let name = List.last identifiers

                    owners
                    |> Map.toList
                    |> List.exists (fun (ownerKey, target) ->
                        Set.contains ownerKey propertyOwners
                        && target.Name = name
                        && (target.TypeKey
                            |> Option.bind (fun typeKey -> Map.tryFind typeKey declaredTypes)
                            |> Option.exists (fun (typeName, _) ->
                                enclosing current.Path
                                |> List.exists (fun prefix -> typeName = prefix @ qualifier))))
            | _ -> false

        let emptyValue =
            { Callables = Set.empty
              Deferred = Set.empty
              Elements = []
              Complete = true }

        let unknownValue = { emptyValue with Complete = false }

        let sequenceParameters =
            facts
            |> List.choose (function
                | SequenceParameter(ownerKey, name) -> Some(ownerKey, name)
                | _ -> None)
            |> Set.ofList

        let joinValues (values: ValueSummary list) =
            let callables =
                values |> List.collect (fun value -> Set.toList value.Callables) |> Set.ofList

            let deferred =
                values |> List.collect (fun value -> Set.toList value.Deferred) |> Set.ofList

            { emptyValue with
                Callables = callables |> Set.toList |> List.truncate 128 |> Set.ofList
                Deferred = deferred |> Set.toList |> List.truncate 128 |> Set.ofList
                Complete =
                    values |> List.forall _.Complete
                    && callables.Count <= 128
                    && deferred.Count <= 128 }

        let positionRange fileName (line, column) =
            Range.mkRange fileName (Position.mkPos line column) (Position.mkPos line (column + 1))

        let rec valueOf visited context current expression accessRange =
            let identity = current.Key, expression, key accessRange

            if Set.contains identity visited || visited.Count >= 128 then
                unknownValue
            else
                let visited = Set.add identity visited

                let evaluate owner expression expressionRange =
                    valueOf visited context owner expression expressionRange

                match expression with
                | ValueOther -> emptyValue
                | ValueFresh -> emptyValue
                | ValueBody(target, deferred) ->
                    let invocation =
                        { Target = target
                          Applied = 0
                          Context = context }

                    if deferred then
                        { emptyValue with
                            Deferred = Set.singleton invocation }
                    else
                        { emptyValue with
                            Callables = Set.singleton invocation }
                | ValueJoin values -> List.map (fun value -> evaluate current value accessRange) values |> joinValues
                | ValueTuple values ->
                    { emptyValue with
                        Elements = List.map (fun value -> evaluate current value accessRange) values }
                | ValueProjection(index, source) ->
                    let value = evaluate current source accessRange

                    if index < value.Elements.Length then
                        value.Elements[index]
                    else
                        unknownValue
                | ValuePath [ root ] ->
                    let reference =
                        lexicalScopes current.Key
                        |> List.tryPick (fun ownerKey -> context |> Map.tryFind (ownerKey, root))

                    match reference with
                    | Some reference ->
                        match Map.tryFind reference.OwnerKey owners with
                        | Some owner ->
                            evaluate owner reference.Expression (positionRange reference.FileName reference.Position)
                        | None -> unknownValue
                    | None ->
                        let binding =
                            lexicalScopes current.Key
                            |> List.tryPick (fun ownerKey ->
                                bindingValues
                                |> Map.tryFind (ownerKey, root)
                                |> Option.bind (
                                    List.filter (fun (_, scope, _) -> Range.rangeContainsRange scope accessRange)
                                    >> List.sortBy (fun (_, scope, _) -> -scope.StartLine, -scope.StartColumn)
                                    >> List.tryHead
                                )
                                |> Option.bind (fun (value, scope, valueRange) ->
                                    Map.tryFind ownerKey owners
                                    |> Option.map (fun owner -> owner, value, scope, valueRange)))

                        match binding with
                        | Some(owner, value, scope, valueRange) ->
                            let initial = evaluate owner value valueRange

                            rebindings
                            |> Map.tryFind (owner.Key, root)
                            |> Option.defaultValue []
                            |> List.fold
                                (fun state (_, value, assignmentRange, joinScope) ->
                                    if not (Range.rangeContainsRange scope assignmentRange) then
                                        state
                                    else
                                        match joinScope with
                                        | Some region when
                                            Range.rangeContainsRange region accessRange
                                            || key assignmentRange < key accessRange
                                            ->
                                            joinValues [ state; evaluate owner value assignmentRange ]
                                        | None when key assignmentRange < key accessRange ->
                                            evaluate owner value assignmentRange
                                        | _ -> state)
                                initial
                        | None ->
                            match resolve Set.empty current [ root ] accessRange with
                            | Some(CallableTarget target) ->
                                match Map.tryFind target.Key arity, Map.tryFind target.Key returnValues with
                                | Some 0, Some((ValueBody(_, false) as returned), returnedRange) ->
                                    evaluate target returned returnedRange
                                | _ ->
                                    { emptyValue with
                                        Callables =
                                            Set.singleton
                                                { Target = target.Key
                                                  Applied = 0
                                                  Context = context } }
                            | Some(Argument(name, _)) when Set.contains (current.Key, name) sequenceParameters ->
                                emptyValue
                            | Some(Captured(ownerKey, name, _)) when Set.contains (ownerKey, name) sequenceParameters ->
                                emptyValue
                            | Some(ModuleValue _) -> emptyValue
                            | _ -> unknownValue
                | ValuePath identifiers ->
                    match resolve Set.empty current identifiers accessRange with
                    | Some(CallableTarget target) ->
                        { emptyValue with
                            Callables =
                                Set.singleton
                                    { Target = target.Key
                                      Applied = 0
                                      Context = context } }
                    | _ -> unknownValue
                | ValueInvoke(ValuePath [ "Seq"; "map" ], [ mapper; source ]) ->
                    let mapper = evaluate current mapper accessRange
                    let source = evaluate current source accessRange

                    { emptyValue with
                        Deferred = Set.union mapper.Callables source.Deferred
                        Complete = mapper.Complete && source.Complete }
                | ValueInvoke(ValuePath [ ("seq" | "async" | "task" | "ref") ], _) -> emptyValue
                | ValueInvoke(callee, arguments) ->
                    let targets = evaluate current callee accessRange

                    let rec apply depth (available: ValueSummary) (arguments: ValueExpr list) =
                        if depth >= 128 then
                            unknownValue
                        else
                            let results =
                                available.Callables
                                |> Set.toList
                                |> List.map (fun invocation ->
                                    let required =
                                        max
                                            0
                                            ((Map.tryFind invocation.Target arity |> Option.defaultValue 0)
                                             - invocation.Applied)

                                    let count = min required arguments.Length

                                    let invocationContext =
                                        parameters
                                        |> Map.tryFind invocation.Target
                                        |> Option.defaultValue []
                                        |> List.fold
                                            (fun state (name, _) ->
                                                let index =
                                                    argumentIndex[invocation.Target, name] - invocation.Applied

                                                if index >= 0 && index < count then
                                                    Map.add
                                                        (invocation.Target, name)
                                                        { OwnerKey = current.Key
                                                          Expression = arguments[index]
                                                          FileName = accessRange.FileName
                                                          Position = key accessRange }
                                                        state
                                                else
                                                    state)
                                            invocation.Context

                                    let invocation =
                                        { invocation with
                                            Applied = invocation.Applied + count
                                            Context = invocationContext }

                                    if count < required then
                                        { emptyValue with
                                            Callables = Set.singleton invocation }
                                    else
                                        match
                                            Map.tryFind invocation.Target owners,
                                            Map.tryFind invocation.Target returnValues
                                        with
                                        | Some target, Some(returned, returnedRange) ->
                                            let result =
                                                valueOf visited invocationContext target returned returnedRange

                                            if count < arguments.Length then
                                                apply (depth + 1) result (List.skip count arguments)
                                            else
                                                result
                                        | _ -> unknownValue)

                            if List.isEmpty results then
                                unknownValue
                            else
                                { joinValues results with
                                    Complete = available.Complete && (results |> List.forall _.Complete) }

                    apply 0 targets arguments

        let storageReturns =
            facts
            |> List.choose (function
                | ReturnValue(ownerKey, _, storage, site) ->
                    storage |> Option.map (fun storage -> ownerKey, (storage, site))
                | _ -> None)
            |> Map.ofList

        let conditionalCalls =
            facts
            |> List.choose (function
                | ConditionalCall(ownerKey, site) -> Some(ownerKey, key site)
                | _ -> None)
            |> Set.ofList

        let storageEvents =
            facts
            |> List.choose (function
                | Storage(ownerKey, name, storage, scope) ->
                    bindingValues
                    |> Map.tryFind (ownerKey, name)
                    |> Option.bind (List.tryFind (fun (_, candidate, _) -> Range.equals scope candidate))
                    |> Option.map (fun (_, _, site) -> ownerKey, (BindStorage(name, storage), site, None, 3))
                | Rebinding(ownerKey, name, storage, _, site, join) ->
                    Some(ownerKey, (RebindStorage(name, storage), site, join, 2))
                | StoredValue(owner, write, site, join) -> Some(owner.Key, (StoreStorage write, site, join, 2))
                | Candidate(owner, Call(callee, arguments), site) ->
                    let join =
                        if Set.contains (owner.Key, key site) conditionalCalls then
                            Some site
                        else
                            None

                    let callee, arguments =
                        match callbackAccess callee arguments with
                        | Some(Call(path, arguments)) -> ValuePath path, arguments
                        | Some(CallValue(value, arguments)) -> value, arguments
                        | _ -> ValuePath callee, arguments

                    Some(owner.Key, (InvokeStorage(callee, arguments), site, join, 1))
                | Candidate(owner, CallValue(callee, arguments), site) ->
                    let join =
                        if Set.contains (owner.Key, key site) conditionalCalls then
                            Some site
                        else
                            None

                    Some(owner.Key, (InvokeStorage(callee, arguments), site, join, 1))
                | _ -> None)
            |> List.groupBy fst
            |> List.map (fun (ownerKey, entries) ->
                ownerKey,
                entries
                |> List.map snd
                |> List.sortBy (fun (_, site, _, order) -> (site.EndLine, site.EndColumn), order))
            |> Map.ofList

        let heapValue origins : HeapValue =
            { Origins = origins
              Locations = Set.empty
              Callables = Set.empty
              Complete = true }

        let freshValue = heapValue (Set.singleton FreshOrigin)

        let unknownHeap name =
            { heapValue (Set.singleton (UnknownOrigin name)) with
                Complete = false }

        let joinHeapValues (values: HeapValue list) : HeapValue =
            { Origins = values |> List.collect (fun value -> Set.toList value.Origins) |> Set.ofList
              Locations = values |> List.collect (fun value -> Set.toList value.Locations) |> Set.ofList
              Callables = values |> List.collect (fun value -> Set.toList value.Callables) |> Set.ofList
              Complete = values |> List.forall _.Complete }

        let emptyState: StorageState =
            { Bindings = Map.empty
              Heap = Map.empty
              Calls = Map.empty
              Complete = true }

        let mergeHeaps (before: StorageState) (after: StorageState) =
            let heap =
                after.Heap
                |> Map.fold
                    (fun heap location cells ->
                        let original = Map.tryFind location heap |> Option.defaultValue Map.empty

                        let cells =
                            cells
                            |> Map.fold
                                (fun merged slot value ->
                                    let original = Map.tryFind slot merged |> Option.defaultValue freshValue
                                    Map.add slot (joinHeapValues [ original; value ]) merged)
                                original

                        Map.add location cells heap)
                    before.Heap

            { after with
                Heap = heap
                Complete = before.Complete && after.Complete }

        let project (state: StorageState) slot (value: HeapValue) =
            let contents =
                value.Locations
                |> Set.toList
                |> List.collect (fun location ->
                    let cells = Map.tryFind location state.Heap |> Option.defaultValue Map.empty

                    let selected =
                        match slot with
                        | FieldSlot _ ->
                            Map.tryFind slot cells
                            |> Option.orElseWith (fun () -> Map.tryFind OtherFieldSlot cells)
                            |> Option.toList
                        | OtherFieldSlot -> Map.tryFind OtherFieldSlot cells |> Option.toList
                        | ElementSlot _ ->
                            [ Map.tryFind slot cells; Map.tryFind AnyElementSlot cells ] |> List.choose id
                        | AnyElementSlot ->
                            cells
                            |> Map.toList
                            |> List.choose (fun (slot, value) ->
                                match slot with
                                | FieldSlot _
                                | OtherFieldSlot -> None
                                | _ -> Some value)

                    if List.isEmpty selected then [ freshValue ] else selected)

            let external =
                if value.Locations.IsEmpty then
                    [ value ]
                else
                    let origins = value.Origins |> Set.remove FreshOrigin
                    if origins.IsEmpty then [] else [ heapValue origins ]

            { joinHeapValues (contents @ external) with
                Complete = value.Complete && (contents |> List.forall _.Complete) }

        let allocate context current site path cells (state: StorageState) =
            let location = current.Key, key site, context, path

            if state.Heap.Count >= 128 && not (Map.containsKey location state.Heap) then
                unknownHeap "storage allocation bound", { state with Complete = false }
            else
                { freshValue with
                    Locations = Set.singleton location },
                { state with
                    Heap = Map.add location cells state.Heap }

        let rec argumentExpression argument =
            let rec fromValue value =
                match value with
                | ValuePath path -> DirectStorage path
                | ValueJoin values -> JoinedStorage(List.map fromValue values)
                | ValueTuple values ->
                    AllocatedStorage(None, values |> List.mapi (fun index value -> ElementSlot index, fromValue value))
                | ValueProjection(index, value) -> ElementStorage(fromValue value, ElementSlot index)
                | ValueInvoke(ValuePath callee, arguments) -> CallResultStorage(callee, List.map fromValue arguments)
                | ValueBody _ -> CallableStorage value
                | ValueFresh
                | ValueOther -> FreshStorage
                | _ -> UnknownStorage "call argument"

            match argument with
            | ArgumentPath path -> DirectStorage path
            | ArgumentValue value -> fromValue value
            | ArgumentReference reference -> fromValue reference.Expression
            | ArgumentCallable owner -> CallableStorage(ValueBody(owner.Key, false))
            | ArgumentOther -> FreshStorage

        let rec evaluateStorage
            (context: (int * int) list)
            current
            expression
            site
            (path: int list)
            (state: StorageState)
            =
            if context.Length >= 128 || path.Length >= 128 then
                unknownHeap "storage evaluation bound", { state with Complete = false }
            else
                let evaluate index expression state =
                    evaluateStorage context current expression site (index :: path) state

                match expression with
                | FreshStorage -> freshValue, state
                | CallableStorage expression ->
                    let bound =
                        match expression with
                        | ValuePath [ name ] ->
                            lexicalScopes current.Key
                            |> List.tryPick (fun ownerKey -> Map.tryFind (ownerKey, name) state.Bindings)
                        | _ -> None

                    match bound with
                    | Some value -> value, state
                    | None ->
                        let value = valueOf Set.empty Map.empty current expression site

                        { freshValue with
                            Callables = value.Callables
                            Complete = value.Complete },
                        state
                | UnknownStorage name -> unknownHeap name, state
                | DirectStorage [] -> freshValue, state
                | DirectStorage(root :: tail) ->
                    let binding =
                        lexicalScopes current.Key
                        |> List.tryPick (fun ownerKey -> Map.tryFind (ownerKey, root) state.Bindings)

                    let value =
                        binding
                        |> Option.defaultWith (fun () ->
                            match resolve Set.empty current [ root ] site with
                            | Some(Argument(name, _)) -> heapValue (Set.singleton (ParameterOrigin name))
                            | Some(Captured(ownerKey, name, _)) ->
                                heapValue (Set.singleton (CaptureOrigin(ownerKey, name)))
                            | Some(Data(source, name, _)) -> heapValue (Set.singleton (SharedOrigin(source, name)))
                            | Some(ModuleValue _) -> freshValue
                            | Some(CallableTarget target) ->
                                { freshValue with
                                    Callables =
                                        Set.singleton
                                            { Target = target.Key
                                              Applied = 0
                                              Context = Map.empty } }
                            | Some Local -> unknownHeap root
                            | Some(Unknown name) -> unknownHeap name
                            | _ -> freshValue)

                    let value =
                        tail
                        |> List.fold
                            (fun value name ->
                                project state (if name = "Item" then AnyElementSlot else FieldSlot name) value)
                            value

                    value, state
                | ElementStorage(source, slot) ->
                    let source, state = evaluate 0 source state
                    project state slot source, state
                | ShallowCopyStorage source ->
                    let source, state = evaluate 0 (DirectStorage source) state

                    let cells =
                        source.Locations
                        |> Set.toList
                        |> List.map (fun location -> Map.tryFind location state.Heap |> Option.defaultValue Map.empty)

                    let cells =
                        cells
                        |> List.fold
                            (fun cells source ->
                                source
                                |> Map.fold
                                    (fun cells slot value ->
                                        let value =
                                            Map.tryFind slot cells
                                            |> Option.map (fun previous -> joinHeapValues [ previous; value ])
                                            |> Option.defaultValue value

                                        Map.add slot value cells)
                                    cells)
                            Map.empty

                    let cells =
                        if source.Locations.IsEmpty then
                            Map.add AnyElementSlot (project state AnyElementSlot source) cells
                        else
                            cells

                    allocate context current site path cells state
                | AllocatedStorage(copied, fields) ->
                    let copied, state =
                        match copied with
                        | Some source -> evaluate 0 source state
                        | None -> freshValue, state

                    let cells =
                        copied.Locations
                        |> Set.toList
                        |> List.fold
                            (fun cells location ->
                                Map.tryFind location state.Heap
                                |> Option.defaultValue Map.empty
                                |> Map.fold (fun cells slot value -> Map.add slot value cells) cells)
                            Map.empty

                    let cells =
                        if copied.Locations.IsEmpty && copied.Origins <> Set.singleton FreshOrigin then
                            Map.add OtherFieldSlot copied cells
                        else
                            cells

                    let cells, state =
                        fields
                        |> List.indexed
                        |> List.fold
                            (fun (cells, state) (index, (slot, expression)) ->
                                let value, state = evaluate (index + 1) expression state
                                Map.add slot value cells, state)
                            (cells, state)

                    allocate context current site path cells state
                | JoinedStorage expressions ->
                    let values, state =
                        expressions
                        |> List.indexed
                        |> List.fold
                            (fun (values, state) (index, expression) ->
                                let value, state = evaluate index expression state
                                value :: values, state)
                            ([], state)

                    joinHeapValues values, state
                | CallResultStorage(callee, arguments) ->
                    invokeStorage context current (ValuePath callee) arguments site state

        and invokeStorage context current callee arguments site (state: StorageState) =
            match Map.tryFind (current.Key, key site) state.Calls with
            | Some value -> value, state
            | None ->
                let functions, state =
                    evaluateStorage context current (CallableStorage callee) site [] state

                let values, state =
                    arguments
                    |> List.indexed
                    |> List.fold
                        (fun (values, state) (index, argument) ->
                            let value, state = evaluateStorage context current argument site [ index ] state
                            values @ [ value ], state)
                        ([], state)

                let results =
                    functions.Callables
                    |> Set.toList
                    |> List.choose (fun invocation ->
                        match Map.tryFind invocation.Target owners with
                        | Some target when
                            context.Length < 128
                            && values.Length + invocation.Applied
                               >= (Map.tryFind target.Key arity |> Option.defaultValue 0)
                            ->
                            let bindings =
                                parameters
                                |> Map.tryFind target.Key
                                |> Option.defaultValue []
                                |> List.mapi (fun index (name, _) ->
                                    let index = index - invocation.Applied

                                    let value =
                                        if index >= 0 && index < values.Length then
                                            values[index]
                                        else
                                            Map.tryFind (target.Key, name) invocation.Context
                                            |> Option.bind (fun reference ->
                                                Map.tryFind reference.OwnerKey owners
                                                |> Option.map (fun owner ->
                                                    evaluateStorage
                                                        context
                                                        owner
                                                        (argumentExpression (ArgumentReference reference))
                                                        site
                                                        []
                                                        state
                                                    |> fst))
                                            |> Option.defaultValue (unknownHeap name)

                                    (target.Key, name), value)
                                |> List.fold (fun bindings (name, value) -> Map.add name value bindings) state.Bindings

                            let entered =
                                { state with
                                    Bindings = bindings
                                    Calls = Map.empty }

                            let result, completed =
                                match Map.tryFind target.Key returnValues with
                                | Some(_, resultSite) ->
                                    let returned =
                                        Map.tryFind target.Key storageReturns
                                        |> Option.map fst
                                        |> Option.defaultValue FreshStorage

                                    let finish = Position.mkPos resultSite.EndLine (resultSite.EndColumn + 1)

                                    let completed =
                                        executeStorage
                                            (key site :: context)
                                            target
                                            (Range.mkRange resultSite.FileName finish finish)
                                            entered

                                    evaluateStorage (key site :: context) target returned resultSite [] completed
                                | None -> unknownHeap target.Name, entered

                            Some(result, completed)
                        | _ -> None)

                match results with
                | [] -> unknownHeap "call result", state
                | results ->
                    let value = results |> List.map fst |> joinHeapValues
                    let completed = results |> List.map snd |> List.reduce mergeHeaps

                    value,
                    { completed with
                        Bindings = state.Bindings
                        Calls = Map.add (current.Key, key site) value state.Calls }

        and executeStorage context current until (state: StorageState) =
            let events = Map.tryFind current.Key storageEvents |> Option.defaultValue []

            events
            |> List.fold
                (fun state (event, site, join, _) ->
                    let before = (site.EndLine, site.EndColumn) < key until

                    let withinLoop =
                        join |> Option.exists (fun region -> Range.rangeContainsRange region until)

                    if not (before || withinLoop) then
                        state
                    else
                        match event with
                        | BindStorage(name, expression)
                        | RebindStorage(name, expression) ->
                            let value, state = evaluateStorage context current expression site [] state

                            let value =
                                if join.IsSome then
                                    Map.tryFind (current.Key, name) state.Bindings
                                    |> Option.map (fun previous -> joinHeapValues [ previous; value ])
                                    |> Option.defaultValue value
                                else
                                    value

                            { state with
                                Bindings = Map.add (current.Key, name) value state.Bindings }
                        | StoreStorage write ->
                            let target, state = evaluateStorage context current write.Receiver site [ 0 ] state
                            let value, state = evaluateStorage context current write.Value site [ 1 ] state

                            let heap =
                                target.Locations
                                |> Set.fold
                                    (fun heap location ->
                                        let cells = Map.tryFind location heap |> Option.defaultValue Map.empty

                                        let weak =
                                            write.Append
                                            || join.IsSome
                                            || target.Locations.Count > 1
                                            || write.Slot = AnyElementSlot

                                        let value =
                                            if weak then
                                                Map.tryFind write.Slot cells
                                                |> Option.map (fun previous -> joinHeapValues [ previous; value ])
                                                |> Option.defaultValue value
                                            else
                                                value

                                        Map.add location (Map.add write.Slot value cells) heap)
                                    state.Heap

                            { state with
                                Heap = heap
                                Complete = state.Complete && target.Complete && value.Complete }
                        | InvokeStorage(callee, arguments) ->
                            let _, completed =
                                invokeStorage context current callee (List.map argumentExpression arguments) site state

                            if join.IsSome then
                                mergeHeaps state completed
                            else
                                completed)
                state

        let summarizeStorage (state: StorageState) (value: HeapValue) =
            let rec reachable visited value =
                let locations = Set.difference value.Locations visited

                if visited.Count >= 128 then
                    Set.singleton (UnknownOrigin "storage reachability bound"), false
                else
                    let visited = Set.union visited locations

                    let children =
                        locations
                        |> Set.toList
                        |> List.collect (fun location ->
                            Map.tryFind location state.Heap
                            |> Option.defaultValue Map.empty
                            |> Map.toList
                            |> List.map snd)

                    children
                    |> List.fold
                        (fun (origins, complete) child ->
                            let childOrigins, childComplete = reachable visited child
                            Set.union origins childOrigins, complete && childComplete)
                        (value.Origins, value.Complete)

            let origins, complete = reachable Set.empty value

            { Outer = value.Origins
              Reachable = origins
              Complete = state.Complete && complete }

        let storageExpression current expression site =
            let state = executeStorage [] current site emptyState
            let value, state = evaluateStorage [] current expression site [] state
            summarizeStorage state value

        let storageOf current identifiers site =
            storageExpression current (DirectStorage identifiers) site

        let mutableCapturedStorage ownerKey name captureRange =
            let owner = owners[ownerKey]

            facts
            |> List.exists (function
                | Candidate(writer, (MutatingCall identifiers | Write(_, _, identifiers)), site) when
                    List.contains ownerKey (lexicalScopes writer.Key)
                    ->
                    let state =
                        executeStorage [] owner (if writer.Key = ownerKey then site else captureRange) emptyState

                    let captured, state =
                        evaluateStorage [] owner (DirectStorage [ name ]) captureRange [] state

                    let written, _ = evaluateStorage [] writer (DirectStorage identifiers) site [] state
                    not (Set.intersect captured.Locations written.Locations).IsEmpty
                | _ -> false)

        let resolveStorage current identifiers site =
            match resolve Set.empty current identifiers site with
            | Some(Captured(ownerKey, name, false)) when mutableCapturedStorage ownerKey name site ->
                Some(Captured(ownerKey, name, true))
            | resolution -> resolution

        { Resolve = resolveStorage
          Storage = storageOf
          StorageValue = storageExpression
          Parameters = parameters
          ArgumentIndex = argumentIndex
          Arity = arity
          ReturnValues = returnValues
          PureConstructors = pureConstructors
          Values = valueOf Set.empty
          OpaqueRead = opaqueRead
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
        | [ "seq" ]
        | [ "task" ]
        | [ "ref" ]
        | [ "Array"; "copy" ]
        | [ "Array"; "set" ]
        | [ "Array"; "map" ]
        | [ "Array"; "length" ]
        | [ "Int32"; "Parse" ]
        | [ "Int64"; "Parse" ]
        | [ "Double"; "Parse" ]
        | [ "Decimal"; "Parse" ]
        | [ "List"; "map" ]
        | [ "List"; "exists" ]
        | [ "List"; "iter" ]
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
                      "failwith"
                      "failwithf"
                      "sprintf"
                      "raise"
                      "None"
                      "Some"
                      "Dictionary"
                      "ResizeArray" ])
            || isLocalConstructor name
        | _ -> false

    let private effectFromOrigin direction origin =
        match direction, origin with
        | OutputFlow, ParameterOrigin name -> [ ParameterWrite name ]
        | InputFlow, CaptureOrigin(ownerKey, name) -> [ CaptureRead(ownerKey, name) ]
        | OutputFlow, CaptureOrigin(ownerKey, name) -> [ CaptureWrite(ownerKey, name) ]
        | OutputFlow, SharedOrigin(TypeState, name) ->
            [ KnownEffect(direction, TypeState, name)
              KnownEffect(direction, ArgumentMutation, name) ]
        | _, SharedOrigin(source, name) -> [ KnownEffect(direction, source, name) ]
        | _, UnknownOrigin name -> [ UnknownEffect(UnknownCall name) ]
        | _ -> []

    let rec private directEffects (resolvers: Resolvers) current access accessRange =
        let make direction source name =
            if direction = OutputFlow && source = TypeState then
                [ KnownEffect(direction, source, name)
                  KnownEffect(direction, ArgumentMutation, name) ]
            else
                [ KnownEffect(direction, source, name) ]

        let resolve current identifiers =
            resolvers.Resolve current identifiers accessRange

        match access with
        | UnsupportedOperation name -> [ UnknownEffect(UnknownCall name) ]
        | MutatingValue expression ->
            resolvers.StorageValue current expression accessRange
            |> _.Outer
            |> Set.toList
            |> List.collect (effectFromOrigin OutputFlow)
        | CallValue(callee, _) ->
            let value = resolvers.Values Map.empty current callee accessRange

            if value.Complete && not value.Callables.IsEmpty then
                []
            else
                [ UnknownEffect(UnknownCall "callable expression") ]
        | Force value ->
            if (resolvers.Values Map.empty current value accessRange).Complete then
                []
            else
                [ UnknownEffect(UnknownCall "deferred value") ]
        | PatternUse name ->
            let active =
                resolvers.Parameters
                |> Map.tryFind current.Key
                |> Option.defaultValue []
                |> List.exists (fun (parameter, _) ->
                    parameter.Contains("|" + name + "|", System.StringComparison.Ordinal))

            if active || name <> "Some" && name <> "None" then
                [ UnknownEffect(UnknownCall name) ]
            else
                []
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
            | Some(Captured(ownerKey, name, true)) -> [ CaptureRead(ownerKey, name) ]
            | Some(Captured _) -> []
            | Some(Unknown _)
            | None -> make direction AmbientEffect name
            | Some(CallableTarget _) -> []
        | Read identifiers ->
            if resolvers.OpaqueRead current identifiers accessRange then
                [ UnknownEffect(UnknownCall(String.concat "." identifiers)) ]
            else
                match resolve current identifiers with
                | Some(Data(source, name, _)) -> make InputFlow source name
                | Some(Captured(ownerKey, name, true)) -> [ CaptureRead(ownerKey, name) ]
                | _ -> []
        | Assign identifiers ->
            match resolve current identifiers with
            | Some(Data(source, name, _)) -> make OutputFlow source name
            | Some(Captured(ownerKey, name, _)) -> [ CaptureWrite(ownerKey, name) ]
            | Some(Unknown name) -> make OutputFlow SharedState name
            | _ -> []
        | Write(identifiers, readsExisting, storageTarget) ->
            let knownInput source name =
                if readsExisting then make InputFlow source name else []

            match resolve current identifiers with
            | Some(Argument(name, _)) -> [ ParameterWrite name ]
            | Some(Data(source, name, _)) -> make OutputFlow source name @ knownInput source name
            | Some(Captured(ownerKey, name, _)) ->
                CaptureWrite(ownerKey, name)
                :: (if readsExisting then
                        [ CaptureRead(ownerKey, name) ]
                    else
                        [])
            | Some(Unknown name) -> make OutputFlow SharedState name
            | _ ->
                let storage = resolvers.Storage current storageTarget accessRange

                let outputs =
                    storage.Outer |> Set.toList |> List.collect (effectFromOrigin OutputFlow)

                let inputs =
                    if readsExisting then
                        storage.Outer |> Set.toList |> List.collect (effectFromOrigin InputFlow)
                    else
                        []

                outputs @ inputs
        | MutatingCall identifiers ->
            match resolve current identifiers with
            | Some(Argument(name, true)) -> [ ParameterWrite name ]
            | Some(Data(source, name, true)) -> make OutputFlow source name
            | Some(Data(source, name, false)) -> make InputFlow source name
            | Some(Captured(ownerKey, name, _)) -> [ CaptureWrite(ownerKey, name) ]
            | _ ->
                let storage = resolvers.Storage current identifiers accessRange

                storage.Outer |> Set.toList |> List.collect (effectFromOrigin OutputFlow)
        | Call(identifiers, arguments) when (callbackAccess identifiers arguments).IsSome ->
            directEffects resolvers current (callbackAccess identifiers arguments |> Option.get) accessRange
        | Call(identifiers, _) ->
            match
                resolve current identifiers, ambientAccess identifiers, isKnownPureCall resolvers current identifiers
            with
            | _, _, _ when identifiers = [ "op_ComposeRight" ] || identifiers = [ "op_ComposeLeft" ] ->
                [ UnknownEffect(UnknownCall "composed callable") ]
            | _, _, _ when identifiers |> List.tryLast |> Option.exists mutatingMethods.Contains -> []
            | Some(Argument(name, _)), _, _ ->
                match access with
                | Call(_, arguments) -> [ ParameterCall(name, arguments) ]
                | _ -> []
            | Some(Captured(ownerKey, name, _)), _, _ ->
                match access with
                | Call(_, arguments) -> [ CaptureCall(ownerKey, name, arguments) ]
                | _ -> []
            | _, Some _, _ -> []
            | _, _, true -> []
            | Some(CallableTarget _), _, _ -> []
            | Some(Local), _, _ ->
                let value = resolvers.Values Map.empty current (ValuePath identifiers) accessRange

                if value.Complete && not value.Callables.IsEmpty then
                    []
                else
                    [ UnknownEffect(UnknownCall(String.concat "." identifiers)) ]
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
        | CaptureRead(_, name) -> make InputFlow SharedState name
        | CaptureWrite(_, name) -> make OutputFlow SharedState name
        | CaptureCall(_, name, _) -> make UnknownFlow UnknownEffectSource name
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

        let argumentExpression =
            function
            | ArgumentPath identifiers -> ValuePath identifiers
            | ArgumentCallable callable -> ValueBody(callable.Key, false)
            | ArgumentValue value -> value
            | ArgumentReference reference -> reference.Expression
            | ArgumentOther -> ValueOther

        let bindArguments current invocation arguments (argumentRange: range) =
            let context =
                resolvers.Parameters
                |> Map.tryFind invocation.Target
                |> Option.defaultValue []
                |> List.fold
                    (fun state (name, _) ->
                        let index = resolvers.ArgumentIndex[invocation.Target, name] - invocation.Applied

                        if index >= 0 && index < List.length arguments then
                            let reference =
                                match arguments[index] with
                                | ArgumentReference reference -> reference
                                | argument ->
                                    { OwnerKey = current.Key
                                      Expression = argumentExpression argument
                                      FileName = argumentRange.FileName
                                      Position = key argumentRange }

                            Map.add (invocation.Target, name) reference state
                        else
                            state)
                    invocation.Context

            { invocation with
                Applied = invocation.Applied + arguments.Length
                Context = context }

        let rec executionTargets depth current (value: ValueSummary) (arguments: CallArgument list) accessRange =
            if depth >= 128 then
                [], false
            else
                let targets =
                    value.Callables
                    |> Set.toList
                    |> List.map (fun target ->
                        let required =
                            max
                                0
                                ((Map.tryFind target.Target resolvers.Arity |> Option.defaultValue 0)
                                 - target.Applied)

                        let count = min required arguments.Length
                        let target = bindArguments current target (List.take count arguments) accessRange

                        if count < required then
                            [], true
                        elif count = arguments.Length then
                            [ target ], true
                        else
                            match Map.tryFind target.Target resolvers.ReturnValues with
                            | Some(returned, returnedRange) ->
                                let returned =
                                    resolvers.Values
                                        target.Context
                                        resolvers.Owners[target.Target]
                                        returned
                                        returnedRange

                                let next, complete =
                                    executionTargets
                                        (depth + 1)
                                        current
                                        returned
                                        (List.skip count arguments)
                                        accessRange

                                target :: next, complete
                            | None -> [ target ], false)

                targets |> List.collect fst,
                value.Complete && not value.Callables.IsEmpty && (targets |> List.forall snd)

        let calls =
            facts
            |> List.collect (function
                | Candidate(current, Call(identifiers, arguments), accessRange) ->
                    let value, callArguments =
                        match callbackAccess identifiers arguments with
                        | Some(Call(identifiers, arguments)) -> ValuePath identifiers, arguments
                        | Some(CallValue(value, arguments)) -> value, arguments
                        | _ -> ValuePath identifiers, arguments

                    let value = resolvers.Values Map.empty current value accessRange
                    let targets, complete = executionTargets 0 current value callArguments accessRange
                    let unresolved = not complete && not value.Callables.IsEmpty
                    [ current, targets, unresolved, accessRange ]
                | Candidate(current, CallValue(value, arguments), accessRange) ->
                    let value = resolvers.Values Map.empty current value accessRange
                    let targets, complete = executionTargets 0 current value arguments accessRange
                    [ current, targets, not complete, accessRange ]
                | Candidate(current, Force value, accessRange) ->
                    resolvers.Values Map.empty current value accessRange
                    |> _.Deferred
                    |> Set.toList
                    |> fun targets -> [ current, targets, false, accessRange ]
                | _ -> [])

        let addEffect ownerKey effect effectRange summaries =
            let current = summaries |> Map.tryFind ownerKey |> Option.defaultValue Map.empty

            match Map.tryFind effect current with
            | Some previous when key previous <= key effectRange -> summaries
            | _ -> summaries |> Map.add ownerKey (current |> Map.add effect effectRange)

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
            |> fun summaries ->
                calls
                |> List.fold
                    (fun state (current, _, unresolved, accessRange) ->
                        if unresolved then
                            addEffect current.Key (UnknownEffect(UnknownCall "returned callable")) accessRange state
                        else
                            state)
                    summaries

        let effectsFor ownerKey summaries =
            summaries |> Map.tryFind ownerKey |> Option.defaultValue Map.empty

        let positionRange fileName (line, column) =
            Range.mkRange fileName (Position.mkPos line column) (Position.mkPos line (column + 1))

        let rec followReference visited context reference =
            match reference.Expression with
            | ValuePath(root :: tail) when not (Set.contains (reference.OwnerKey, root) visited) ->
                match Map.tryFind (reference.OwnerKey, root) context with
                | Some actual ->
                    let expression =
                        match actual.Expression with
                        | ValuePath path -> ValuePath(boundedPath (path @ tail))
                        | _ when List.isEmpty tail -> actual.Expression
                        | _ -> ValueOther

                    followReference
                        (Set.add (reference.OwnerKey, root) visited)
                        context
                        { actual with Expression = expression }
                | None -> reference
            | _ -> reference

        let referenceEffects caller direction context reference callRange =
            let reference = followReference Set.empty context reference

            match Map.tryFind reference.OwnerKey resolvers.Owners, reference.Expression with
            | _, ValueFresh -> []
            | Some owner, ValuePath identifiers ->
                resolvers.Storage owner identifiers (positionRange reference.FileName reference.Position)
                |> _.Outer
                |> Set.toList
                |> List.collect (fun origin ->
                    match origin with
                    | ParameterOrigin name when owner.Key <> caller.Key ->
                        [ if direction = InputFlow then
                              CaptureRead(owner.Key, name)
                          else
                              CaptureWrite(owner.Key, name) ]
                    | FreshOrigin -> []
                    | _ -> effectFromOrigin direction origin)
                |> List.map (fun effect -> effect, callRange)
            | _, ValueOther -> []
            | _ -> [ UnknownEffect(UnknownCall "argument storage"), callRange ]

        let rec transferEffect visited summaries caller invocation (callRange: range) (originalRange: range) effect =
            let identity = invocation, effect

            if Set.contains identity visited || visited.Count >= 128 then
                [ UnknownEffect(UnknownCall "recursive callback"), callRange ]
            else
                let visited = Set.add identity visited

                let reference ownerKey name =
                    Map.tryFind (ownerKey, name) invocation.Context

                let transferCallback ownerKey name arguments =
                    let arguments =
                        arguments
                        |> List.map (fun argument ->
                            let reference =
                                match argument with
                                | ArgumentReference reference -> reference
                                | argument ->
                                    { OwnerKey = invocation.Target
                                      Expression = argumentExpression argument
                                      FileName = originalRange.FileName
                                      Position = key originalRange }

                            ArgumentReference(followReference Set.empty invocation.Context reference))

                    let targetReference =
                        reference ownerKey name
                        |> Option.defaultValue
                            { OwnerKey = ownerKey
                              Expression = ValuePath [ name ]
                              FileName = originalRange.FileName
                              Position = key originalRange }
                        |> followReference Set.empty invocation.Context

                    match Map.tryFind targetReference.OwnerKey resolvers.Owners with
                    | None -> [ UnknownEffect(UnknownCall name), callRange ]
                    | Some owner ->
                        let value =
                            resolvers.Values
                                invocation.Context
                                owner
                                targetReference.Expression
                                (positionRange targetReference.FileName targetReference.Position)

                        let targets, complete = executionTargets 0 owner value arguments originalRange

                        let transferred =
                            targets
                            |> List.collect (fun target ->
                                effectsFor target.Target summaries
                                |> Map.toList
                                |> List.collect (fun (effect, effectRange) ->
                                    transferEffect visited summaries caller target callRange effectRange effect))

                        if complete then
                            transferred
                        else
                            let pending =
                                match targetReference.Expression with
                                | ValuePath [ actual ] when targetReference.OwnerKey = caller.Key ->
                                    ParameterCall(actual, arguments)
                                | ValuePath [ actual ] -> CaptureCall(targetReference.OwnerKey, actual, arguments)
                                | _ -> UnknownEffect(UnknownCall name)

                            (pending, callRange) :: transferred

                match effect with
                | KnownEffect _
                | UnknownEffect _ -> [ effect, callRange ]
                | CaptureRead(ownerKey, name)
                | CaptureWrite(ownerKey, name) ->
                    let direction =
                        match effect with
                        | CaptureRead _ -> InputFlow
                        | _ -> OutputFlow

                    match reference ownerKey name with
                    | Some reference -> referenceEffects caller direction invocation.Context reference callRange
                    | None when caller.Key = ownerKey ->
                        resolvers.Storage caller [ name ] callRange
                        |> _.Outer
                        |> Set.toList
                        |> List.collect (effectFromOrigin direction)
                        |> List.map (fun effect -> effect, callRange)
                    | None -> [ effect, callRange ]
                | ParameterWrite name ->
                    match reference invocation.Target name with
                    | Some reference -> referenceEffects caller OutputFlow invocation.Context reference callRange
                    | None -> [ UnknownEffect(UnknownCall name), callRange ]
                | ParameterCall(name, arguments) -> transferCallback invocation.Target name arguments
                | CaptureCall(ownerKey, name, arguments) -> transferCallback ownerKey name arguments

        let rec propagate iteration summaries =
            if iteration > facts.Length + 1 then
                summaries
            else
                let next =
                    calls
                    |> List.fold
                        (fun state (caller, targets, _, callRange) ->
                            targets
                            |> List.collect (fun invocation ->
                                effectsFor invocation.Target summaries
                                |> Map.toList
                                |> List.collect (fun (effect, originalRange) ->
                                    transferEffect
                                        Set.empty
                                        summaries
                                        caller
                                        invocation
                                        callRange
                                        originalRange
                                        effect))
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

        produced
        |> List.sortBy (fun item -> item.Location.StartLine, item.Location.StartColumn)
        |> List.distinctBy (fun item -> item.OwnerLine, item.Owner, item.Direction, item.Source, item.Name)
