namespace MessFSharp

open System
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
module SyntaxModel =
    type DeclarationFactKind =
        | NamespaceFact
        | ModuleFact
        | TypeFact of TypeShape
        | BindingFact

    type DeclarationFact =
        { Name: string
          Kind: DeclarationFactKind
          Location: SourceLocation
          IsMutable: bool
          ParameterCount: int
          Parameters: (string * SourceLocation) list }

    type Facts =
        { Declarations: DeclarationFact list
          Expressions: NormalizedExpression list
          LexicalScopes: LexicalScope list
          References: SyntacticReference list }

    let private location fileName (range: range) =
        { File = fileName
          StartLine = max 1 range.StartLine
          StartColumn = max 1 (range.StartColumn + 1)
          EndLine = max 1 range.EndLine
          EndColumn = max 1 (range.EndColumn + 1) }

    let rec private patternName pattern =
        match pattern with
        | SynPat.Named(SynIdent(identifier, _), _, _, _) -> Some identifier.idText
        | SynPat.LongIdent(longDotId = longIdentifier) ->
            longIdentifier.LongIdent
            |> List.tryLast
            |> Option.map (fun identifier -> identifier.idText)
        | SynPat.Typed(pat = nested)
        | SynPat.Attrib(pat = nested)
        | SynPat.Paren(pat = nested)
        | SynPat.FromParseError(pat = nested) -> patternName nested
        | SynPat.As(lhsPat = left; rhsPat = right) ->
            patternName left |> Option.orElseWith (fun () -> patternName right)
        | _ -> None

    let rec private logicalParameterCount pattern =
        match pattern with
        | SynPat.Tuple(elementPats = elements) -> elements |> List.sumBy logicalParameterCount
        | SynPat.Paren(pat = nested)
        | SynPat.Typed(pat = nested)
        | SynPat.Attrib(pat = nested) -> logicalParameterCount nested
        | _ -> 1

    let rec private parameterPatterns pattern =
        match pattern with
        | SynPat.Named(SynIdent(identifier, _), _, _, patternRange) -> [ identifier.idText, patternRange ]
        | SynPat.Tuple(elementPats = elements) -> elements |> List.collect parameterPatterns
        | SynPat.Paren(pat = nested)
        | SynPat.Typed(pat = nested)
        | SynPat.Attrib(pat = nested) -> parameterPatterns nested
        | SynPat.As(lhsPat = left; rhsPat = right) -> parameterPatterns left @ parameterPatterns right
        | _ -> []

    let private bindingParameters fileName pattern =
        let patterns =
            match pattern with
            | SynPat.LongIdent(argPats = SynArgPats.Pats values) -> values
            | _ -> []

        patterns
        |> List.collect parameterPatterns
        |> List.map (fun (name, patternRange) -> name, location fileName patternRange)

    let private bindingParameterCount pattern =
        match pattern with
        | SynPat.LongIdent(argPats = SynArgPats.Pats patterns) -> patterns |> List.sumBy logicalParameterCount
        | SynPat.LongIdent(argPats = SynArgPats.NamePatPairs(pats = patterns)) -> patterns.Length
        | _ -> 0

    let private simpleTypeShape representation =
        match representation with
        | SynTypeDefnSimpleRepr.Record _ -> RecordType
        | SynTypeDefnSimpleRepr.Union _ -> UnionType
        | SynTypeDefnSimpleRepr.TypeAbbrev _ -> TypeAbbreviation
        | SynTypeDefnSimpleRepr.General(kind = SynTypeDefnKind.Interface) -> InterfaceType
        | SynTypeDefnSimpleRepr.General(kind = SynTypeDefnKind.Class) -> ClassType
        | SynTypeDefnSimpleRepr.General(kind = SynTypeDefnKind.Struct) -> StructType
        | _ -> OtherType

    let private typeShape representation =
        match representation with
        | SynTypeDefnRepr.Simple(simpleRepr = simple) -> simpleTypeShape simple
        | SynTypeDefnRepr.ObjectModel(kind = SynTypeDefnKind.Interface) -> InterfaceType
        | SynTypeDefnRepr.ObjectModel(kind = SynTypeDefnKind.Struct) -> StructType
        | SynTypeDefnRepr.ObjectModel(kind = SynTypeDefnKind.Unspecified; members = members) when
            not (List.isEmpty members)
            && members |> List.forall (fun memberDefinition -> memberDefinition.IsAbstractSlot)
            ->
            InterfaceType
        | SynTypeDefnRepr.ObjectModel _ -> ClassType
        | _ -> OtherType

    let private expressionKind (expression: SynExpr) =
        if expression.IsIfThenElse then
            ConditionalExpression
        elif expression.IsMatch || expression.IsMatchLambda || expression.IsMatchBang then
            MatchExpression
        elif
            expression.IsWhile
            || expression.IsWhileBang
            || expression.IsFor
            || expression.IsForEach
        then
            LoopExpression
        elif expression.IsTryWith then
            ExceptionHandlerExpression
        elif expression.IsLambda then
            LambdaExpression
        elif expression.IsComputationExpr then
            ComputationExpression
        elif expression.IsSet || expression.IsLongIdentSet || expression.IsDotSet then
            AssignmentExpression
        elif expression.IsApp then
            ApplicationExpression
        else
            OrdinaryExpression

    let private referenceName (expression: SynExpr) =
        match expression with
        | SynExpr.Ident identifier -> Some identifier.idText
        | SynExpr.LongIdent(longDotId = longIdentifier) ->
            longIdentifier.LongIdent
            |> List.tryLast
            |> Option.map (fun identifier -> identifier.idText)
        | _ -> None

    let private contains outerRange innerRange =
        let startsBefore =
            outerRange.StartLine < innerRange.StartLine
            || (outerRange.StartLine = innerRange.StartLine
                && outerRange.StartColumn <= innerRange.StartColumn)

        let endsAfter =
            outerRange.EndLine > innerRange.EndLine
            || (outerRange.EndLine = innerRange.EndLine
                && outerRange.EndColumn >= innerRange.EndColumn)

        startsBefore && endsAfter && outerRange <> innerRange

    let private withParents (scopes: LexicalScope list) =
        scopes
        |> List.map (fun (scope: LexicalScope) ->
            let parent =
                scopes
                |> List.filter (fun (candidate: LexicalScope) -> contains candidate.Location scope.Location)
                |> List.sortBy (fun candidate ->
                    candidate.Location.EndLine - candidate.Location.StartLine,
                    candidate.Location.EndColumn - candidate.Location.StartColumn)
                |> List.tryHead
                |> Option.map (fun candidate -> candidate.Location)

            { scope with Parent = parent })

    let normalize fileName (parsedInput: ParsedInput) =
        let folder (declarations, expressions, scopes, references) _ node =
            match node with
            | SyntaxNode.SynModuleOrNamespace(SynModuleOrNamespace(longId = identifiers; kind = kind; range = nodeRange)) ->
                let name =
                    identifiers
                    |> List.map (fun identifier -> identifier.idText)
                    |> String.concat "."

                let factKind =
                    match kind with
                    | SynModuleOrNamespaceKind.DeclaredNamespace
                    | SynModuleOrNamespaceKind.GlobalNamespace -> NamespaceFact
                    | _ -> ModuleFact

                let fact =
                    { Name = name
                      Kind = factKind
                      Location = location fileName nodeRange
                      IsMutable = false
                      ParameterCount = 0
                      Parameters = [] }

                fact :: declarations,
                expressions,
                { Location = fact.Location
                  Parent = None }
                :: scopes,
                references
            | SyntaxNode.SynTypeDefn(SynTypeDefn(
                typeInfo = SynComponentInfo(longId = identifiers); typeRepr = representation; range = nodeRange)) ->
                let fact =
                    { Name =
                        identifiers
                        |> List.map (fun identifier -> identifier.idText)
                        |> String.concat "."
                      Kind = TypeFact(typeShape representation)
                      Location = location fileName nodeRange
                      IsMutable = false
                      ParameterCount = 0
                      Parameters = [] }

                fact :: declarations,
                expressions,
                { Location = fact.Location
                  Parent = None }
                :: scopes,
                references
            | SyntaxNode.SynBinding(SynBinding(headPat = pattern; isMutable = isMutable; range = nodeRange)) ->
                match patternName pattern with
                | Some name ->
                    let fact =
                        let parameters = bindingParameters fileName pattern

                        { Name = name
                          Kind = BindingFact
                          Location = location fileName nodeRange
                          IsMutable = isMutable
                          ParameterCount = bindingParameterCount pattern
                          Parameters = parameters }

                    fact :: declarations,
                    expressions,
                    { Location = fact.Location
                      Parent = None }
                    :: scopes,
                    references
                | None -> declarations, expressions, scopes, references
            | SyntaxNode.SynMatchClause clause ->
                let expression =
                    { Kind = MatchClauseExpression
                      Location = location fileName clause.Range }

                declarations, expression :: expressions, scopes, references
            | SyntaxNode.SynExpr expression ->
                let normalized =
                    { Kind = expressionKind expression
                      Location = location fileName expression.Range }

                let nextScopes =
                    if expression.IsLambda || expression.IsComputationExpr then
                        { Location = normalized.Location
                          Parent = None }
                        :: scopes
                    else
                        scopes

                let nextReferences =
                    match referenceName expression with
                    | Some name ->
                        { Name = name
                          Location = normalized.Location }
                        :: references
                    | None -> references

                declarations, normalized :: expressions, nextScopes, nextReferences
            | _ -> declarations, expressions, scopes, references

        let declarations, expressions, scopes, references =
            ParsedInput.fold folder ([], [], [], []) parsedInput

        { Declarations = declarations |> List.rev
          Expressions = expressions |> List.rev
          LexicalScopes =
            scopes
            |> List.distinctBy (fun (scope: LexicalScope) -> scope.Location)
            |> withParents
          References = references |> List.rev }
