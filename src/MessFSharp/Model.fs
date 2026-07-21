namespace MessFSharp

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessivePublicCount")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyMethods")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CountInLoopExpression")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveParameterList")>]
module Model =
    let private declarationRegex pattern =
        Regex(pattern, RegexOptions.Compiled ||| RegexOptions.CultureInvariant)

    let private modulePattern = declarationRegex "^\\s*(module|namespace)\\s+([^\\s=]+)"

    let private typePattern =
        declarationRegex "^\\s*type\\s+(``[^`]+``|[A-Za-z_][\\w'.]*)"

    let private signatureValuePattern =
        declarationRegex "^\\s*val\\s+(``[^`]+``|[A-Za-z_][\\w']*)\\s*:"

    let private memberPattern =
        declarationRegex
            "^\\s*(?:(public|private|internal|protected)\\s+)?(?:(static)\\s+)?(?:(abstract\\s+member|override|default\\s+member|member)\\s+)(.+?)(?:\\s*=|\\s+with|\\s*:|$)"

    let private letPattern =
        declarationRegex
            "^\\s*let\\s+(?:(public|private|internal)\\s+)?(?:(inline|rec)\\s+)?(?:(mutable)\\s+)?(.+?)\\s*="

    let private andPattern = declarationRegex "^\\s*and\\s+(?:(mutable)\\s+)?(.+?)\\s*="

    let private recordFieldPattern =
        declarationRegex "(?:^|[;{])\\s*(?:\\[<[^>]+>\\]\\s*)?([A-Za-z_][\\w']*)\\s*:"

    let private classFieldPattern =
        declarationRegex "^\\s*(?:(static)\\s+)?(?:let|val)\\s+(?:(mutable)\\s+)?([A-Za-z_][\\w']*)\\s*(?::|=)"

    let private unionCasePattern = declarationRegex "^\\s*\\|\\s*([A-Za-z_][\\w']*)"

    let private suppressionPattern =
        declarationRegex "SuppressMessage(?:Attribute)?\\s*\\(\\s*\"[^\"]*\"\\s*,\\s*\"([^\"]+)\""

    let private namePattern =
        declarationRegex "(?:``[^`]+``|[A-Za-z_][\\w']*|[!%&*+\\-./<=>?@^|~:]+)"

    let private indentation (line: string) =
        line
        |> Seq.takeWhile (fun character -> character = ' ' || character = '\t')
        |> Seq.length

    let private isMeaningfulLine (line: string) =
        let trimmed = line.Trim()

        not (String.IsNullOrWhiteSpace trimmed)
        && not (trimmed.StartsWith("//", StringComparison.Ordinal))

    let private lineAt (lines: string array) lineNumber =
        if lineNumber >= 1 && lineNumber <= lines.Length then
            lines[lineNumber - 1]
        else
            ""

    let private sourceLocation (source: SourceFile) startLine endLine startColumn =
        let endText = lineAt source.Lines endLine

        { File = source.FullPath
          StartLine = startLine
          StartColumn = max 1 startColumn
          EndLine = endLine
          EndColumn = max 1 (endText.Length + 1) }

    let private sourceText (source: SourceFile) startLine endLine =
        source.Lines
        |> Array.skip (max 0 (startLine - 1))
        |> Array.take (max 0 (min source.Lines.Length endLine - max 0 (startLine - 1)))
        |> String.concat Environment.NewLine

    let private matchGroup (matchResult: Match) (groupName: string) =
        if matchResult.Success && matchResult.Groups[groupName].Success then
            Some(matchResult.Groups[groupName].Value)
        else
            None

    let private precedingAttributes (lines: string array) lineNumber =
        let collected = ResizeArray<string>()
        let mutable index = lineNumber - 2
        let mutable keepGoing = true

        while index >= 0 && keepGoing do
            let text = lines[index].Trim()

            if
                String.IsNullOrWhiteSpace text
                || text.StartsWith("[<", StringComparison.Ordinal)
            then
                collected.Add(lines[index])
                index <- index - 1
            else
                keepGoing <- false

        collected |> Seq.rev |> String.concat "\n"

    let private suppressedRules (lines: string array) lineNumber =
        let attributes = precedingAttributes lines lineNumber

        suppressionPattern.Matches(attributes)
        |> Seq.cast<Match>
        |> Seq.choose (fun item -> matchGroup item "1")
        |> Set.ofSeq

    let private removeDecorations (value: string) =
        value.Trim()
        |> fun text ->
            if text.StartsWith("inline ", StringComparison.Ordinal) then
                text.Substring(7)
            else
                text
        |> fun text ->
            if text.StartsWith("rec ", StringComparison.Ordinal) then
                text.Substring(4)
            else
                text
        |> fun text ->
            if text.StartsWith("mutable ", StringComparison.Ordinal) then
                text.Substring(8)
            else
                text

    let private firstName (value: string) =
        let matchResult = namePattern.Match(removeDecorations value)

        if matchResult.Success then
            let name = matchResult.Value

            if Set.contains name (set [ "inline"; "rec"; "mutable"; "fun"; "function"; "this"; "self" ]) then
                None
            else
                Some name
        else
            None

    let private memberName (value: string) =
        let cleaned =
            value.Trim()
            |> fun text -> Regex.Replace(text, "^(public|private|internal|protected)\\s+", "")
            |> fun text ->
                if text.StartsWith("val ", StringComparison.Ordinal) then
                    text.Substring(4)
                else
                    text

        let dotted =
            Regex.Match(
                cleaned,
                "(?:this|self|base|[A-Za-z_][\\w']*)\\s*\\.\\s*(``[^`]+``|[A-Za-z_][\\w']*|[!%&*+\\-./<=>?@^|~:]+)"
            )

        if dotted.Success then
            Some dotted.Groups[1].Value
        else
            firstName cleaned

    let private scopeEnd (source: SourceFile) startLine startIndent kind =
        let mutable result = source.Lines.Length
        let mutable index = startLine
        let mutable found = false

        while index < source.Lines.Length && not found do
            let candidate = source.Lines[index]
            let trimmed = candidate.Trim()

            if isMeaningfulLine candidate then
                let candidateIndent = indentation candidate

                let unionContinuation =
                    (kind = Type && trimmed.StartsWith("|", StringComparison.Ordinal))
                    || (kind = Function && trimmed.StartsWith("and ", StringComparison.Ordinal))

                if candidateIndent <= startIndent && not unionContinuation then
                    result <- index
                    found <- true

            index <- index + 1

        result

    let private moduleScopeEnd (source: SourceFile) startLine startIndent =
        let mutable result = source.Lines.Length
        let mutable index = startLine
        let mutable found = false

        while index < source.Lines.Length && not found do
            let candidate = source.Lines[index]

            if isMeaningfulLine candidate && indentation candidate <= startIndent then
                let trimmed = candidate.TrimStart()

                if
                    trimmed.StartsWith("module ", StringComparison.Ordinal)
                    || trimmed.StartsWith("namespace ", StringComparison.Ordinal)
                then
                    result <- index
                    found <- true

            index <- index + 1

        result

    let private isLiteralDeclaration (lines: string array) lineNumber =
        let attributes = precedingAttributes lines lineNumber
        attributes.IndexOf("Literal", StringComparison.OrdinalIgnoreCase) >= 0

    let private isCompilerGeneratedDeclaration (lines: string array) lineNumber =
        let attributes = precedingAttributes lines lineNumber
        attributes.IndexOf("CompilerGenerated", StringComparison.OrdinalIgnoreCase) >= 0

    let private isBooleanText (text: string) =
        Regex.IsMatch(text, "(?i)(:|->)\\s*bool\\b")
        || Regex.IsMatch(text, "(?i)\\b(true|false)\\b")

    let private signatureParameterCount (line: string) =
        let arrowCount = Regex.Matches(line, "->").Count

        if arrowCount = 0 then
            0
        else
            let firstArrow = line.IndexOf("->", StringComparison.Ordinal)
            let firstParameterGroup = line.Substring(0, firstArrow)
            let tupleParameters = firstParameterGroup.Split('*').Length
            max arrowCount tupleParameters

    let private parseParameterNames (source: SourceFile) lineNumber declarationName lineText =
        let sourceForLine =
            { FullPath = source.FullPath
              Kind = source.Kind
              Text = lineText
              Lines = [| lineText |] }

        let tokens = Scanner.scan sourceForLine

        let nameIndex =
            tokens |> Array.tryFindIndex (fun token -> token.Text = declarationName)

        match nameIndex with
        | None -> []
        | Some index ->
            let parameters = ResizeArray<string * int>()
            let mutable inType = false
            let mutable doneWithParameters = false
            let mutable angleDepth = 0

            for tokenIndex in index + 1 .. tokens.Length - 1 do
                let token = tokens[tokenIndex]

                if not doneWithParameters then
                    if not inType && (token.Text = "=" || token.Text = "->") then
                        doneWithParameters <- true
                    elif token.Text = ":" then
                        inType <- true
                    elif inType && token.Text = "<" then
                        angleDepth <- angleDepth + 1
                    elif inType && token.Text = ">" then
                        angleDepth <- max 0 (angleDepth - 1)
                    elif inType && token.Text = ")" && angleDepth = 0 then
                        inType <- false
                    elif inType && token.Text = "," && angleDepth = 0 then
                        inType <- false
                    elif not inType && token.Kind = Identifier then
                        if token.Text <> "this" && token.Text <> "self" && token.Text <> "base" then
                            parameters.Add(token.Text, token.Column)

            parameters |> Seq.toList

    let private makeDeclaration
        (source: SourceFile)
        (name: string)
        kind
        line
        startColumn
        parent
        parentKind
        (accessibility: string)
        isMutable
        isStatic
        isLiteral
        isRecord
        isUnion
        isClassLike
        isFunction
        isModuleLevel
        parameterCount
        scopeStart
        scopeEnd
        bodyStart
        bodyEnd
        (text: string)
        =
        let normalizedName = name.Trim('`')

        { Name = normalizedName
          Kind = kind
          Location = sourceLocation source line bodyEnd startColumn
          Parent = parent
          ParentKind = parentKind
          Accessibility = accessibility
          IsMutable = isMutable
          IsStatic = isStatic
          IsPrivate = accessibility = "private"
          IsPublic = accessibility = "public" || accessibility = ""
          IsCompilerGenerated = isCompilerGeneratedDeclaration source.Lines line
          IsIgnored = normalizedName = "_" || normalizedName.StartsWith("_", StringComparison.Ordinal)
          IsLiteral = isLiteral
          IsRecord = isRecord
          IsUnion = isUnion
          IsClassLike = isClassLike
          IsFunction = isFunction
          IsModuleLevel = isModuleLevel
          IsBoolean = isBooleanText text
          SuppressedRules = suppressedRules source.Lines line
          ParameterCount = parameterCount
          ScopeStartLine = scopeStart
          ScopeEndLine = scopeEnd
          BodyStartLine = bodyStart
          BodyEndLine = bodyEnd
          Text = text }

    let private nearestParent (declarations: Declaration list) line =
        declarations
        |> List.filter (fun declaration ->
            (declaration.Kind = Module
             || declaration.Kind = Namespace
             || declaration.Kind = Type)
            && declaration.Location.StartLine < line
            && declaration.ScopeStartLine <= line
            && declaration.ScopeEndLine >= line)
        |> List.sortByDescending (fun declaration -> declaration.Location.StartLine)
        |> List.tryHead

    let private enclosingBody (declarations: Declaration list) line =
        declarations
        |> List.filter (fun declaration ->
            (declaration.Kind = Function || declaration.Kind = Member)
            && declaration.Location.StartLine < line
            && declaration.ScopeStartLine <= line
            && declaration.ScopeEndLine >= line)
        |> List.sortByDescending (fun declaration -> declaration.Location.StartLine)
        |> List.tryHead

    let private tokenCount (tokens: SyntaxToken array) name startLine endLine =
        tokens
        |> Array.filter (fun token -> token.Text = name && token.Line >= startLine && token.Line <= endLine)
        |> Array.length

    let private complexity (tokens: SyntaxToken array) startLine endLine =
        let within token =
            token.Line >= startLine && token.Line <= endLine

        let branchWords = set [ "if"; "for"; "while"; "try"; "when" ]

        let wordBranches =
            tokens
            |> Array.filter within
            |> Array.sumBy (fun token -> if branchWords.Contains(token.Text) then 1 else 0)

        let matchBranches =
            tokens
            |> Array.filter within
            |> Array.sumBy (fun token -> if token.Text = "match" then 1 else 0)

        let caseBranches =
            tokens
            |> Array.filter within
            |> Array.sumBy (fun token -> if token.Text = "|" then 1 else 0)

        let shortCircuitBranches =
            tokens
            |> Array.filter within
            |> Array.sumBy (fun token -> if token.Text = "&&" || token.Text = "||" then 1 else 0)

        max
            1
            (1
             + wordBranches
             + matchBranches
             + max 0 (caseBranches - matchBranches)
             + shortCircuitBranches)

    let private nPath complexityValue =
        let mutable result = 1L

        for _ in 1 .. min 30 complexityValue do
            result <- min Int64.MaxValue (result * 2L)

        if result > int64 Int32.MaxValue then
            Int32.MaxValue
        else
            int result

    let private lineCount startLine endLine = max 1 (endLine - startLine + 1)

    let private buildBaseDeclarations source =
        let declarations = ResizeArray<Declaration>()

        for lineNumber in 1 .. source.Lines.Length do
            let line = source.Lines[lineNumber - 1]
            let trimmed = line.Trim()
            let indent = indentation line

            let add
                name
                kind
                accessibility
                isMutable
                isStatic
                isRecord
                isUnion
                isClassLike
                isFunction
                isModuleLevel
                parameterCount
                scopeEndLine
                =
                let declarationText = sourceText source lineNumber scopeEndLine

                declarations.Add(
                    makeDeclaration
                        source
                        name
                        kind
                        lineNumber
                        (indent + 1)
                        None
                        None
                        accessibility
                        isMutable
                        isStatic
                        (isLiteralDeclaration source.Lines lineNumber)
                        isRecord
                        isUnion
                        isClassLike
                        isFunction
                        isModuleLevel
                        parameterCount
                        lineNumber
                        scopeEndLine
                        lineNumber
                        scopeEndLine
                        declarationText
                )

            let moduleMatch = modulePattern.Match(line)

            if moduleMatch.Success then
                let kind =
                    if moduleMatch.Groups[1].Value = "module" then
                        Module
                    else
                        Namespace

                let endLine =
                    if kind = Module then
                        moduleScopeEnd source lineNumber indent
                    else
                        scopeEnd source lineNumber indent kind

                add moduleMatch.Groups[2].Value kind "" false false false false false false true 0 endLine
            else
                let typeMatch = typePattern.Match(line)

                if typeMatch.Success then
                    let endLine = scopeEnd source lineNumber indent Type
                    let body = sourceText source lineNumber endLine

                    let isRecord =
                        body.IndexOf("{", StringComparison.Ordinal) >= 0
                        && body.IndexOf("}", StringComparison.Ordinal) >= 0

                    let isUnion = body.IndexOf("|", StringComparison.Ordinal) >= 0

                    let isClassLike =
                        not isRecord
                        && not isUnion
                        && (body.IndexOf("member", StringComparison.Ordinal) >= 0
                            || body.IndexOf("class", StringComparison.Ordinal) >= 0
                            || body.IndexOf("let ", StringComparison.Ordinal) >= 0)

                    add typeMatch.Groups[1].Value Type "" false false isRecord isUnion isClassLike false true 0 endLine
                else
                    let signatureMatch =
                        if source.Kind = Signature then
                            signatureValuePattern.Match(line)
                        else
                            Match.Empty

                    if signatureMatch.Success then
                        let name = signatureMatch.Groups[1].Value
                        let endLine = scopeEnd source lineNumber indent Function
                        let parent = nearestParent (declarations |> Seq.toList) lineNumber

                        let moduleLevel =
                            match parent with
                            | Some parent when parent.Kind = Type -> false
                            | _ -> enclosingBody (declarations |> Seq.toList) lineNumber |> Option.isNone

                        let declarationText = sourceText source lineNumber endLine

                        declarations.Add(
                            makeDeclaration
                                source
                                name
                                Function
                                lineNumber
                                (indent + 1)
                                (parent |> Option.map (fun item -> item.Name))
                                (parent |> Option.map (fun item -> item.Kind))
                                ""
                                false
                                false
                                (isLiteralDeclaration source.Lines lineNumber)
                                false
                                false
                                false
                                true
                                moduleLevel
                                (signatureParameterCount line)
                                lineNumber
                                endLine
                                lineNumber
                                endLine
                                declarationText
                        )
                    else
                        let memberMatch = memberPattern.Match(line)

                        if memberMatch.Success then
                            let memberText = memberMatch.Groups[4].Value

                            let memberAccess =
                                Regex.Match(memberText, "^(public|private|internal|protected)\\s+")

                            match memberName memberText with
                            | Some name ->
                                let endLine = scopeEnd source lineNumber indent Member

                                let accessibility =
                                    defaultArg
                                        (matchGroup memberMatch "1")
                                        (if memberAccess.Success then
                                             memberAccess.Groups[1].Value
                                         else
                                             "")

                                let isStatic = (matchGroup memberMatch "2").IsSome
                                let parameterCount = parseParameterNames source lineNumber name line |> List.length

                                let kind =
                                    if
                                        memberMatch.Groups[4].Value.IndexOf(" with", StringComparison.Ordinal) >= 0
                                        || memberMatch.Groups[4].Value.IndexOf("member val", StringComparison.Ordinal)
                                           >= 0
                                    then
                                        Property
                                    else
                                        Member

                                add
                                    name
                                    kind
                                    accessibility
                                    false
                                    isStatic
                                    false
                                    false
                                    true
                                    (parameterCount > 0)
                                    false
                                    parameterCount
                                    endLine
                            | None -> ()
                        else
                            let letMatch = letPattern.Match(line)

                            if letMatch.Success then
                                match firstName letMatch.Groups[4].Value with
                                | Some name ->
                                    let endLine = scopeEnd source lineNumber indent Function
                                    let parameterCount = parseParameterNames source lineNumber name line |> List.length

                                    let isFunction =
                                        parameterCount > 0
                                        || letMatch.Groups[4].Value.IndexOf("fun ", StringComparison.Ordinal) >= 0
                                        || letMatch.Groups[4].Value.IndexOf("function", StringComparison.Ordinal) >= 0

                                    let accessibility = defaultArg (matchGroup letMatch "1") ""
                                    let isMutable = (matchGroup letMatch "3").IsSome
                                    let parent = nearestParent (declarations |> Seq.toList) lineNumber

                                    let moduleLevel =
                                        match parent with
                                        | Some parent when parent.Kind = Type -> false
                                        | _ -> enclosingBody (declarations |> Seq.toList) lineNumber |> Option.isNone

                                    let kind = if isFunction then Function else Value
                                    let declarationText = sourceText source lineNumber endLine

                                    declarations.Add(
                                        makeDeclaration
                                            source
                                            name
                                            kind
                                            lineNumber
                                            (indent + 1)
                                            (parent |> Option.map (fun item -> item.Name))
                                            (parent |> Option.map (fun item -> item.Kind))
                                            accessibility
                                            isMutable
                                            false
                                            (isLiteralDeclaration source.Lines lineNumber)
                                            false
                                            false
                                            false
                                            isFunction
                                            moduleLevel
                                            parameterCount
                                            lineNumber
                                            endLine
                                            lineNumber
                                            endLine
                                            declarationText
                                    )
                                | None ->
                                    let andMatch = andPattern.Match(line)

                                    if andMatch.Success then
                                        match firstName andMatch.Groups[2].Value with
                                        | Some name ->
                                            let endLine = scopeEnd source lineNumber indent Function

                                            let parameterCount =
                                                parseParameterNames source lineNumber name line |> List.length

                                            let parent = nearestParent (declarations |> Seq.toList) lineNumber

                                            let moduleLevel =
                                                match parent with
                                                | Some parent when parent.Kind = Type -> false
                                                | _ ->
                                                    enclosingBody (declarations |> Seq.toList) lineNumber
                                                    |> Option.isNone

                                            let declarationText = sourceText source lineNumber endLine

                                            declarations.Add(
                                                makeDeclaration
                                                    source
                                                    name
                                                    Function
                                                    lineNumber
                                                    (indent + 1)
                                                    (parent |> Option.map (fun item -> item.Name))
                                                    (parent |> Option.map (fun item -> item.Kind))
                                                    ""
                                                    false
                                                    false
                                                    false
                                                    false
                                                    false
                                                    false
                                                    true
                                                    moduleLevel
                                                    parameterCount
                                                    lineNumber
                                                    endLine
                                                    lineNumber
                                                    endLine
                                                    declarationText
                                            )
                                        | None -> ()

        declarations |> Seq.toList

    let private addUnionCases source declarations =
        let result = ResizeArray<Declaration>()

        for declaration in declarations do
            result.Add(declaration)

        for typeDeclaration in
            declarations
            |> List.filter (fun declaration -> declaration.Kind = Type && declaration.IsUnion) do
            for lineNumber in typeDeclaration.Location.StartLine + 1 .. typeDeclaration.ScopeEndLine do
                let line = lineAt source.Lines lineNumber
                let caseMatch = unionCasePattern.Match(line)

                if caseMatch.Success then
                    let name = caseMatch.Groups[1].Value

                    result.Add(
                        makeDeclaration
                            source
                            name
                            UnionCase
                            lineNumber
                            (indentation line + 1)
                            (Some typeDeclaration.Name)
                            (Some Type)
                            ""
                            false
                            false
                            false
                            false
                            true
                            false
                            false
                            false
                            0
                            lineNumber
                            lineNumber
                            lineNumber
                            lineNumber
                            (line.Trim())
                    )

        result |> Seq.toList

    let private addFields source declarations =
        let result = ResizeArray<Declaration>()

        for declaration in declarations do
            result.Add(declaration)

        for typeDeclaration in declarations |> List.filter (fun declaration -> declaration.Kind = Type) do
            let firstLine =
                if typeDeclaration.IsRecord then
                    typeDeclaration.Location.StartLine
                else
                    typeDeclaration.Location.StartLine + 1

            for lineNumber in firstLine .. typeDeclaration.ScopeEndLine do
                let line = lineAt source.Lines lineNumber

                let fieldMatches =
                    if typeDeclaration.IsRecord then
                        recordFieldPattern.Matches(line) |> Seq.cast<Match> |> Seq.toList
                    else
                        let fieldMatch = classFieldPattern.Match(line)

                        if fieldMatch.Success then [ fieldMatch ] else []

                for fieldMatch in fieldMatches do
                    let name =
                        if typeDeclaration.IsRecord then
                            fieldMatch.Groups[1].Value
                        else
                            fieldMatch.Groups[3].Value

                    if name <> "with" && name <> "get" && name <> "set" then
                        let isMutable =
                            not typeDeclaration.IsRecord
                            && line.IndexOf("mutable", StringComparison.Ordinal) >= 0

                        let isStatic = not typeDeclaration.IsRecord && fieldMatch.Groups[1].Value = "static"
                        let accessibility = if typeDeclaration.IsRecord then "" else "private"

                        result.Add(
                            makeDeclaration
                                source
                                name
                                Field
                                lineNumber
                                (indentation line + 1)
                                (Some typeDeclaration.Name)
                                (Some Type)
                                accessibility
                                isMutable
                                isStatic
                                false
                                false
                                false
                                false
                                false
                                false
                                0
                                lineNumber
                                lineNumber
                                lineNumber
                                lineNumber
                                (line.Trim())
                        )

        result |> Seq.toList

    let private addParameters source declarations =
        let result = ResizeArray<Declaration>()

        for declaration in declarations do
            result.Add(declaration)

            if declaration.Kind = Function || declaration.Kind = Member then
                let line = lineAt source.Lines declaration.Location.StartLine

                let parameterNames =
                    parseParameterNames source declaration.Location.StartLine declaration.Name line

                for parameterName, parameterColumn in parameterNames do
                    result.Add(
                        makeDeclaration
                            source
                            parameterName
                            Parameter
                            declaration.Location.StartLine
                            parameterColumn
                            (Some declaration.Name)
                            (Some declaration.Kind)
                            ""
                            false
                            false
                            false
                            false
                            false
                            false
                            false
                            false
                            0
                            declaration.ScopeStartLine
                            declaration.ScopeEndLine
                            declaration.BodyStartLine
                            declaration.BodyEndLine
                            (line.Trim())
                    )

        result |> Seq.toList

    let private applyParents declarations =
        declarations
        |> List.map (fun declaration ->
            match declaration.Kind with
            | UnionCase
            | Field
            | Parameter -> declaration
            | _ ->
                match nearestParent declarations declaration.Location.StartLine with
                | None -> declaration
                | Some parent when
                    parent.Name = declaration.Name
                    && parent.Location.StartLine = declaration.Location.StartLine
                    ->
                    declaration
                | Some parent ->
                    { declaration with
                        Parent = Some parent.Name
                        ParentKind = Some parent.Kind
                        IsModuleLevel = declaration.IsModuleLevel })

    let analyze (source: SourceFile) =
        let tokens = Scanner.scan source
        let baseDeclarations = buildBaseDeclarations source
        let withCases = addUnionCases source baseDeclarations
        let withFields = addFields source withCases
        let withParameters = addParameters source withFields
        let declarations = applyParents withParameters

        let referenceCounts =
            declarations
            |> List.map (fun declaration -> declaration.Name, tokenCount tokens declaration.Name 1 source.Lines.Length)
            |> Map.ofList

        let mutatedNames =
            tokens
            |> Array.windowed 2
            |> Array.choose (fun pair ->
                if pair[1].Text = "<-" || pair[1].Text = ":=" then
                    Some pair[0].Text
                else
                    None)
            |> Set.ofArray

        let measuredDeclarations =
            declarations
            |> List.filter (fun declaration ->
                declaration.Kind = Function
                || declaration.Kind = Member
                || declaration.Kind = Type
                || declaration.Kind = Module)

        let complexityByDeclaration =
            measuredDeclarations
            |> List.map (fun declaration ->
                (declaration.Name, declaration.Location.StartLine),
                complexity tokens declaration.BodyStartLine declaration.BodyEndLine)
            |> Map.ofList

        let nPathByDeclaration =
            complexityByDeclaration |> Map.map (fun _ value -> nPath value)

        let lineCountByDeclaration =
            measuredDeclarations
            |> List.map (fun declaration ->
                (declaration.Name, declaration.Location.StartLine),
                lineCount declaration.BodyStartLine declaration.BodyEndLine)
            |> Map.ofList

        let typeFields =
            declarations
            |> List.filter (fun declaration -> declaration.Kind = Field)
            |> List.groupBy (fun declaration -> defaultArg declaration.Parent "")
            |> Map.ofList

        let typeMethods =
            declarations
            |> List.filter (fun declaration -> declaration.Kind = Member || declaration.Kind = Function)
            |> List.groupBy (fun declaration -> defaultArg declaration.Parent "")
            |> Map.ofList

        { Source = source
          Tokens = tokens
          Declarations = declarations
          ComplexityByDeclaration = complexityByDeclaration
          NPathByDeclaration = nPathByDeclaration
          LineCountByDeclaration = lineCountByDeclaration
          ReferenceCounts = referenceCounts
          MutatedNames = mutatedNames
          TypeFields = typeFields
          TypeMethods = typeMethods }
