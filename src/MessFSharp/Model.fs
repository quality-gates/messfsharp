namespace MessFSharp

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open FSharp.Compiler.Syntax
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessivePublicCount")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyMethods")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyPublicMethods")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CountInLoopExpression")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveParameterList")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassLength")>]
module Model =
    type private ParameterInfo =
        { Name: string
          Line: int
          Column: int
          IsBoolean: bool }

    let private declarationRegex pattern =
        Regex(pattern, RegexOptions.Compiled ||| RegexOptions.CultureInvariant)

    let private modulePattern =
        declarationRegex
            "^\\s*(?:(?<accessibility>private|internal|public)\\s+)?(?<kind>module|namespace)\\s+(?:(?:rec\\s+)|(?<accessibility>private|internal|public)\\s+)*(?<name>[^\\s=]+)"

    let private typePattern =
        declarationRegex
            "^\\s*type\\s+(?:(?<accessibility>private|internal|public)\\s+|(?:abstract|sealed|rec)\\s+)*(?<name>``[^`]+``|[A-Za-z_][\\w'.]*)"

    let private signatureValuePattern =
        declarationRegex "^\\s*val\\s+(``[^`]+``|[A-Za-z_][\\w']*)\\s*:"

    let private memberPattern =
        declarationRegex
            "^\\s*(?:(public|private|internal|protected)\\s+)?(?:(static)\\s+)?(?:(abstract\\s+member|abstract|override|default\\s+member|member)\\s+)(.+?)(?:\\s*=|\\s+with|\\s*:|$)"

    let private letPattern =
        declarationRegex
            "^\\s*let\\s+(?:(?:(?<accessibility>public|private|internal)|(?<modifier>inline|rec)|(?<mutable>mutable))\\s+)*(?<binding>.+?)\\s*="

    let private andPattern = declarationRegex "^\\s*and\\s+(?:(mutable)\\s+)?(.+?)\\s*="

    let private recordFieldPattern =
        declarationRegex "(?:^|[;{])\\s*(?:\\[<[^>]+>\\]\\s*)?(``[^`]+``|[A-Za-z_][\\w']*)\\s*:"

    let private classFieldPattern =
        declarationRegex
            "^\\s*(?:(static)\\s+)?(?:let|val)\\s+(?:(mutable)\\s+)?(``[^`]+``|[A-Za-z_][\\w']*)\\s*(?::|=)"

    let private unionCasePattern =
        declarationRegex "\\|\\s*(``[^`]+``|[A-Za-z_][\\w']*)"

    let private firstUnionCasePattern =
        declarationRegex "=\\s*(``[^`]+``|[A-Za-z_][\\w']*)"

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
                    Regex.IsMatch(
                        trimmed,
                        "^(?:(?:private|internal|public)\\s+)?(?:module|namespace)(?:\\s+(?:rec|private|internal|public))*\\b"
                    )
                then
                    result <- index
                    found <- true

            index <- index + 1

        result

    let private namespaceScopeEnd (source: SourceFile) startLine startIndent =
        let mutable result = source.Lines.Length
        let mutable index = startLine
        let mutable found = false

        while index < source.Lines.Length && not found do
            let candidate = source.Lines[index]

            if isMeaningfulLine candidate && indentation candidate <= startIndent then
                let trimmed = candidate.TrimStart()

                if
                    Regex.IsMatch(
                        trimmed,
                        "^(?:(?:private|internal|public)\\s+)?namespace(?:\\s+rec|\\s+private|\\s+internal|\\s+public)*\\b"
                    )
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

    let private parseParameterInfos (text: string) declarationName =
        let sourceForDeclaration =
            { FullPath = "<declaration>"
              Kind = Implementation
              Text = text
              Lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n') }

        let tokens = Scanner.scan sourceForDeclaration

        let nameIndex =
            tokens |> Array.tryFindIndex (fun token -> token.Text = declarationName)

        let isOperatorSyntaxName =
            declarationName
            |> Seq.exists (fun character -> "!%&*+-./<=>?@^|~:".IndexOf(character) >= 0)

        match nameIndex with
        | None -> []
        | Some _ when isOperatorSyntaxName -> []
        | Some index ->
            let parameters = ResizeArray<ParameterInfo>()
            let mutable currentName: (string * int * int) option = None
            let mutable currentIsBoolean = false
            let mutable inType = false
            let mutable parenthesisDepth = 0
            let mutable bracketDepth = 0
            let mutable braceDepth = 0
            let mutable angleDepth = 0
            let mutable doneWithParameters = false

            let flush () =
                match currentName with
                | Some(name, line, column) ->
                    parameters.Add(
                        { Name = name
                          Line = line
                          Column = column
                          IsBoolean = currentIsBoolean }
                    )
                | None -> ()

                currentName <- None
                currentIsBoolean <- false

            let isTopLevel () =
                parenthesisDepth = 0 && bracketDepth = 0 && braceDepth = 0

            let ignoredIdentifier value =
                Set.contains
                    value
                    (set
                        [ "as"
                          "base"
                          "do"
                          "done"
                          "else"
                          "for"
                          "function"
                          "if"
                          "in"
                          "let"
                          "match"
                          "member"
                          "new"
                          "of"
                          "or"
                          "override"
                          "rec"
                          "self"
                          "static"
                          "then"
                          "this"
                          "try"
                          "type"
                          "use"
                          "when"
                          "with" ])

            if index + 1 < tokens.Length then
                for tokenIndex in index + 1 .. tokens.Length - 1 do
                    let token = tokens[tokenIndex]

                    if not doneWithParameters then
                        match token.Text with
                        | "=" when isTopLevel () ->
                            flush ()
                            doneWithParameters <- true
                        | "->" when isTopLevel () && not inType ->
                            flush ()
                            doneWithParameters <- true
                        | ":" when not (parenthesisDepth = 0 && bracketDepth = 0 && braceDepth = 0) -> inType <- true
                        | ":" ->
                            flush ()
                            inType <- true
                        | "," when inType && angleDepth = 0 ->
                            inType <- false
                            flush ()
                        | ("," | ";") when not inType -> flush ()
                        | ")" ->
                            if parenthesisDepth > 0 then
                                parenthesisDepth <- parenthesisDepth - 1

                            if inType then
                                inType <- false

                            flush ()
                        | "]" ->
                            if bracketDepth > 0 then
                                bracketDepth <- bracketDepth - 1

                            if inType then
                                inType <- false

                            flush ()
                        | "}" ->
                            if braceDepth > 0 then
                                braceDepth <- braceDepth - 1

                            if inType then
                                inType <- false

                            flush ()
                        | "(" ->
                            if not inType && isTopLevel () then
                                flush ()

                            parenthesisDepth <- parenthesisDepth + 1
                        | "[" ->
                            if not inType && isTopLevel () then
                                flush ()

                            bracketDepth <- bracketDepth + 1
                        | "{" ->
                            if not inType && isTopLevel () then
                                flush ()

                            braceDepth <- braceDepth + 1
                        | "<" -> angleDepth <- angleDepth + 1
                        | ">" when angleDepth > 0 -> angleDepth <- angleDepth - 1
                        | _ when angleDepth > 0 -> ()
                        | _ when inType ->
                            if token.Text.Equals("bool", StringComparison.OrdinalIgnoreCase) then
                                currentIsBoolean <- true
                        | _ when token.Kind = Identifier ->
                            let value = token.Text.Trim('`')

                            if not (ignoredIdentifier value) && value <> "_" then
                                match currentName with
                                | Some(prev, _, _) when prev.Length > 0 && Char.IsUpper(prev[0]) -> ()
                                | Some _ -> flush ()
                                | None -> ()

                                currentName <- Some(value, token.Line, token.Column)
                        | _ -> ()

                flush ()

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
          IsInterface = false
          TypeShape =
            if isRecord then RecordType
            elif isUnion then UnionType
            elif isClassLike then ClassType
            else NotAType
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

    let private referenceScope (declarations: Declaration list) (declaration: Declaration) sourceLineCount =
        if declaration.Kind = Field then
            declarations
            |> List.filter (fun candidate ->
                candidate.Kind = Type
                && candidate.Name = defaultArg declaration.Parent ""
                && candidate.ScopeStartLine <= declaration.Location.StartLine
                && candidate.ScopeEndLine >= declaration.Location.StartLine)
            |> List.sortByDescending (fun candidate -> candidate.ScopeStartLine)
            |> List.tryHead
            |> Option.map (fun owner -> owner.ScopeStartLine, owner.ScopeEndLine)
            |> Option.defaultValue (declaration.ScopeStartLine, declaration.ScopeEndLine)
        elif
            declaration.Kind = Parameter
            || ((declaration.Kind = Value || declaration.Kind = Function)
                && not declaration.IsModuleLevel)
        then
            declarations
            |> List.filter (fun candidate ->
                (candidate.Kind = Function || candidate.Kind = Member)
                && candidate.Location.StartLine < declaration.Location.StartLine
                && candidate.ScopeStartLine <= declaration.Location.StartLine
                && candidate.ScopeEndLine >= declaration.Location.StartLine)
            |> List.sortByDescending (fun candidate -> candidate.Location.StartLine)
            |> List.tryHead
            |> Option.map (fun enclosing -> enclosing.ScopeStartLine, enclosing.ScopeEndLine)
            |> Option.defaultValue (declaration.ScopeStartLine, declaration.ScopeEndLine)
        else
            1, sourceLineCount

    let private referenceCountFor
        (tokens: SyntaxToken array)
        (declarations: Declaration list)
        (target: Declaration)
        sourceLineCount
        =
        let sameDeclaration (candidate: Declaration) =
            candidate.Name = target.Name
            && candidate.Kind = target.Kind
            && candidate.Location.StartLine = target.Location.StartLine

        let resolvesAt (token: SyntaxToken) =
            declarations
            |> List.filter (fun (candidate: Declaration) ->
                candidate.Name = target.Name
                && candidate.Location.StartLine <= token.Line
                && not (
                    candidate.Kind = Value
                    && not candidate.IsFunction
                    && token.Line <= candidate.BodyEndLine
                )
                && (let startLine, endLine = referenceScope declarations candidate sourceLineCount
                    token.Line >= startLine && token.Line <= endLine))
            |> List.sortWith (fun (left: Declaration) (right: Declaration) ->
                let leftStart, _ = referenceScope declarations left sourceLineCount
                let rightStart, _ = referenceScope declarations right sourceLineCount

                if leftStart <> rightStart then
                    compare rightStart leftStart
                else
                    compare right.Location.StartLine left.Location.StartLine)
            |> List.tryHead
            |> Option.exists sameDeclaration

        let declarationReference =
            if target.Kind = Value && not target.IsFunction then
                1
            else
                0

        declarationReference
        + (tokens
           |> Array.sumBy (fun (token: SyntaxToken) ->
               if token.Text = target.Name && resolvesAt token then
                   1
               else
                   0))

    let private complexity (tokens: SyntaxToken array) startLine endLine =
        let within (token: SyntaxToken) =
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

    let private nPath (tokens: SyntaxToken array) startLine endLine =
        let within (token: SyntaxToken) =
            token.Line >= startLine && token.Line <= endLine

        let scopedTokens = tokens |> Array.filter within

        let count tokenText =
            scopedTokens
            |> Array.sumBy (fun token -> if token.Text = tokenText then 1 else 0)

        let multiply left right =
            if right > 0 && left > Int32.MaxValue / right then
                Int32.MaxValue
            else
                left * right

        let add left right =
            if right > Int32.MaxValue - left then
                Int32.MaxValue
            else
                left + right

        let powerOfTwo exponent =
            [ 1..exponent ] |> List.fold (fun value _ -> multiply value 2) 1

        let ifStarts = ResizeArray<int>()
        let ifThens = ResizeArray<int option>()
        let ifElses = ResizeArray<int option>()
        let stack = ResizeArray<int>()

        let findTopmost predicate =
            let mutable position = stack.Count - 1
            let mutable found = None

            while position >= 0 && found.IsNone do
                let candidate = stack[position]

                if predicate candidate then
                    found <- Some(position, candidate)
                else
                    position <- position - 1

            found

        for index in 0 .. scopedTokens.Length - 1 do
            match scopedTokens[index].Text with
            | "if" ->
                ifStarts.Add(index)
                ifThens.Add(None)
                ifElses.Add(None)
                stack.Add(ifStarts.Count - 1)
            | "then" ->
                match findTopmost (fun candidate -> ifThens[candidate].IsNone) with
                | Some(_, candidate) -> ifThens[candidate] <- Some index
                | None -> ()
            | "else" ->
                match
                    findTopmost (fun candidate ->
                        ifThens[candidate].IsSome
                        && ifElses[candidate].IsNone
                        && scopedTokens[ifStarts[candidate]].Column <= scopedTokens[index].Column)
                with
                | Some(stackPosition, candidate) ->
                    ifElses[candidate] <- Some index
                    stack.RemoveAt(stackPosition)
                | None -> ()
            | _ -> ()

        let findEnd startIndex anchorIndex =
            let startColumn = scopedTokens[startIndex].Column
            let anchorLine = scopedTokens[anchorIndex].Line
            let mutable index = anchorIndex + 1
            let mutable result = scopedTokens.Length

            while index < scopedTokens.Length && result = scopedTokens.Length do
                let token = scopedTokens[index]

                if token.Text = ";" || (token.Line > anchorLine && token.Column <= startColumn) then
                    result <- index
                else
                    index <- index + 1

            result

        let decisions =
            [ 0 .. ifStarts.Count - 1 ]
            |> List.choose (fun index ->
                match ifThens[index] with
                | Some thenIndex ->
                    let anchorIndex = defaultArg ifElses[index] thenIndex

                    Some(ifStarts[index], thenIndex, ifElses[index], findEnd ifStarts[index] anchorIndex)
                | None -> None)

        let rec paths startIndex endExclusive =
            let topLevelDecisions =
                decisions
                |> List.filter (fun decision ->
                    let decisionStartIndex, _, _, decisionEndExclusive = decision

                    decisionStartIndex >= startIndex
                    && decisionStartIndex < endExclusive
                    && not (
                        decisions
                        |> List.exists (fun parent ->
                            let parentStartIndex, _, _, parentEndExclusive = parent

                            parentStartIndex >= startIndex
                            && parentStartIndex < decisionStartIndex
                            && parentEndExclusive > decisionStartIndex
                            && parentEndExclusive <= endExclusive)
                    ))

            topLevelDecisions
            |> List.fold
                (fun result decision ->
                    let _decisionStartIndex, thenIndex, elseIndex, decisionEndExclusive = decision
                    let thenEnd = defaultArg elseIndex decisionEndExclusive
                    let thenPaths = paths (thenIndex + 1) thenEnd

                    let elsePaths =
                        elseIndex
                        |> Option.map (fun elseIndex -> paths (elseIndex + 1) decisionEndExclusive)
                        |> Option.defaultValue 1

                    multiply result (add thenPaths elsePaths))
                1

        let binaryDecisions =
            count "for" + count "while" + count "try" + count "&&" + count "||"

        let ifPaths =
            if List.isEmpty decisions then
                1
            else
                paths 0 scopedTokens.Length

        let matchStarts =
            scopedTokens
            |> Array.mapi (fun index token -> index, token)
            |> Array.choose (fun (index, token) ->
                if token.Text = "match" || token.Text = "function" then
                    Some(index, token)
                else
                    None)

        let matchPathFactors =
            matchStarts
            |> Array.mapi (fun matchIndex (matchTokenIndex, matchToken) ->
                let owned tokenText =
                    scopedTokens
                    |> Array.mapi (fun tokenIndex token -> tokenIndex, token)
                    |> Array.choose (fun (tokenIndex, token) ->
                        if
                            token.Text = tokenText
                            && tokenIndex > matchTokenIndex
                            && token.Column >= matchToken.Column
                        then
                            Some(tokenIndex, token)
                        else
                            None)
                    |> Array.filter (fun (tokenIndex, token) ->
                        matchStarts
                        |> Array.mapi (fun candidateIndex (candidateTokenIndex, candidateToken) ->
                            candidateIndex, candidateTokenIndex, candidateToken)
                        |> Array.filter (fun (_, candidateTokenIndex, candidateToken) ->
                            candidateTokenIndex < tokenIndex && candidateToken.Column <= token.Column)
                        |> Array.tryLast
                        |> Option.map (fun (candidateIndex, _, _) -> candidateIndex = matchIndex)
                        |> Option.defaultValue false)
                    |> Array.length

                max 1 (owned "|" + owned "when"))

        let matchPaths = matchPathFactors |> Array.fold multiply 1

        multiply (multiply ifPaths (powerOfTwo binaryDecisions)) matchPaths

    let private lineCount startLine endLine = max 1 (endLine - startLine + 1)

    let private buildBaseDeclarations source =
        let declarations = ResizeArray<Declaration>()

        for lineNumber in 1 .. source.Lines.Length do
            let line = source.Lines[lineNumber - 1]
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
                    if moduleMatch.Groups["kind"].Value = "module" then
                        Module
                    else
                        Namespace

                let endLine =
                    if kind = Module then
                        moduleScopeEnd source lineNumber indent
                    else
                        namespaceScopeEnd source lineNumber indent

                add
                    moduleMatch.Groups["name"].Value
                    kind
                    (defaultArg (matchGroup moduleMatch "accessibility") "")
                    false
                    false
                    false
                    false
                    false
                    false
                    true
                    0
                    endLine
            else
                let typeMatch = typePattern.Match(line)

                if typeMatch.Success then
                    let endLine = scopeEnd source lineNumber indent Type
                    let body = sourceText source lineNumber endLine
                    let bodyLines = source.Lines[(lineNumber - 1) .. (endLine - 1)]

                    let isRecord =
                        Regex.IsMatch(line, "=\\s*(?:struct\\s+)?\\{")
                        || (bodyLines
                            |> Array.exists (fun bodyLine ->
                                bodyLine.TrimStart().StartsWith("{", StringComparison.Ordinal)))

                    let isUnion =
                        bodyLines
                        |> Array.mapi (fun offset bodyLine -> lineNumber + offset, bodyLine)
                        |> Array.exists (fun (bodyLineNumber, bodyLine) ->
                            (bodyLineNumber = lineNumber
                             && Regex.IsMatch(bodyLine, "=.+\\|\\s*(``[^`]+``|[A-Za-z_][\\w']*)"))
                            || (bodyLineNumber > lineNumber
                                && indentation bodyLine <= indent + 4
                                && unionCasePattern.IsMatch(bodyLine)))

                    let isClassLike =
                        not isRecord
                        && not isUnion
                        && (Regex.IsMatch(line, "^\\s*type\\s+.*\\([^)]*\\)\\s*=")
                            || body.IndexOf("member", StringComparison.Ordinal) >= 0
                            || body.IndexOf("class", StringComparison.Ordinal) >= 0
                            || body.IndexOf("let ", StringComparison.Ordinal) >= 0
                            || body.IndexOf("abstract", StringComparison.Ordinal) >= 0)

                    add
                        typeMatch.Groups["name"].Value
                        Type
                        (defaultArg (matchGroup typeMatch "accessibility") "")
                        false
                        false
                        isRecord
                        isUnion
                        isClassLike
                        false
                        true
                        0
                        endLine
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
                                let declarationText = sourceText source lineNumber endLine

                                let accessibility =
                                    defaultArg
                                        (matchGroup memberMatch "1")
                                        (if memberAccess.Success then
                                             memberAccess.Groups[1].Value
                                         else
                                             "")

                                let isStatic = (matchGroup memberMatch "2").IsSome
                                let parsedParameterCount = parseParameterInfos declarationText name |> List.length

                                let parameterCount =
                                    if parsedParameterCount = 0 && line.Contains("->", StringComparison.Ordinal) then
                                        max parsedParameterCount (signatureParameterCount line)
                                    else
                                        parsedParameterCount

                                let kind =
                                    let propertyLike =
                                        line.IndexOf("member val", StringComparison.OrdinalIgnoreCase) >= 0
                                        || line.IndexOf(" with", StringComparison.OrdinalIgnoreCase) >= 0
                                        || (parameterCount = 0
                                            && memberText.IndexOf('(') < 0
                                            && (line.Contains("=", StringComparison.Ordinal)
                                                || line.Contains(":", StringComparison.Ordinal)))

                                    if propertyLike then Property else Member

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
                                match firstName letMatch.Groups["binding"].Value with
                                | Some name ->
                                    let endLine = scopeEnd source lineNumber indent Function
                                    let declarationText = sourceText source lineNumber endLine
                                    let parameterCount = parseParameterInfos declarationText name |> List.length
                                    let bindingText = letMatch.Groups["binding"].Value.Trim()

                                    let hasExplicitParameterGroup =
                                        bindingText.Length > name.Length
                                        && bindingText
                                            .Substring(name.Length)
                                            .TrimStart()
                                            .StartsWith("(", StringComparison.Ordinal)

                                    let isFunction =
                                        parameterCount > 0
                                        || hasExplicitParameterGroup
                                        || bindingText.IndexOf("fun ", StringComparison.Ordinal) >= 0
                                        || bindingText.IndexOf("function", StringComparison.Ordinal) >= 0

                                    let accessibility = defaultArg (matchGroup letMatch "accessibility") ""
                                    let isMutable = (matchGroup letMatch "mutable").IsSome
                                    let parent = nearestParent (declarations |> Seq.toList) lineNumber

                                    let moduleLevel =
                                        match parent with
                                        | Some parent when parent.Kind = Type -> false
                                        | _ -> enclosingBody (declarations |> Seq.toList) lineNumber |> Option.isNone

                                    let kind = if isFunction then Function else Value

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
                                            let declarationText = sourceText source lineNumber endLine

                                            let parameterCount = parseParameterInfos declarationText name |> List.length

                                            let parent = nearestParent (declarations |> Seq.toList) lineNumber

                                            let moduleLevel =
                                                match parent with
                                                | Some parent when parent.Kind = Type -> false
                                                | _ ->
                                                    enclosingBody (declarations |> Seq.toList) lineNumber
                                                    |> Option.isNone

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
            let typeIndent =
                indentation (lineAt source.Lines typeDeclaration.Location.StartLine)

            let lineNumbers =
                if typeDeclaration.Location.StartLine <= typeDeclaration.ScopeEndLine then
                    [ typeDeclaration.Location.StartLine .. typeDeclaration.ScopeEndLine ]
                else
                    []

            for lineNumber in lineNumbers do
                let line = lineAt source.Lines lineNumber

                if indentation line <= typeIndent + 4 then
                    if lineNumber = typeDeclaration.Location.StartLine then
                        let firstCaseMatch = firstUnionCasePattern.Match(line)

                        if firstCaseMatch.Success then
                            let name = firstCaseMatch.Groups[1].Value

                            result.Add(
                                makeDeclaration
                                    source
                                    name
                                    UnionCase
                                    lineNumber
                                    (firstCaseMatch.Groups[1].Index + 1)
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

                    for caseMatch in unionCasePattern.Matches(line) |> Seq.cast<Match> do
                        let name = caseMatch.Groups[1].Value

                        result.Add(
                            makeDeclaration
                                source
                                name
                                UnionCase
                                lineNumber
                                (caseMatch.Groups[1].Index + 1)
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

            let lineNumbers =
                if firstLine <= typeDeclaration.ScopeEndLine then
                    [ firstLine .. typeDeclaration.ScopeEndLine ]
                else
                    []

            for lineNumber in lineNumbers do
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

    let private constructorParameterText (text: string) name =
        let openIndex = text.IndexOf('(')
        let closeIndex = text.LastIndexOf(')')

        if openIndex >= 0 && closeIndex > openIndex then
            sprintf "let %s %s =" name (text.Substring(openIndex, closeIndex - openIndex + 1))
        else
            sprintf "let %s =" name

    let private addConstructors source declarations =
        let result = ResizeArray<Declaration>()

        for declaration in declarations do
            result.Add(declaration)

        for typeDeclaration in
            declarations
            |> List.filter (fun declaration -> declaration.Kind = Type && declaration.IsClassLike) do
            let header = lineAt source.Lines typeDeclaration.Location.StartLine
            let openIndex = header.IndexOf('(')
            let equalsIndex = header.IndexOf('=')

            if openIndex >= 0 && equalsIndex > openIndex then
                let synthetic = constructorParameterText header typeDeclaration.Name

                let parameterCount =
                    parseParameterInfos synthetic typeDeclaration.Name |> List.length

                result.Add(
                    makeDeclaration
                        source
                        typeDeclaration.Name
                        Constructor
                        typeDeclaration.Location.StartLine
                        (openIndex + 1)
                        (Some typeDeclaration.Name)
                        (Some Type)
                        ""
                        false
                        false
                        false
                        false
                        false
                        false
                        false
                        false
                        parameterCount
                        typeDeclaration.ScopeStartLine
                        typeDeclaration.ScopeEndLine
                        typeDeclaration.BodyStartLine
                        typeDeclaration.BodyEndLine
                        header
                )

        result |> Seq.toList

    let private addParameters source declarations =
        let result = ResizeArray<Declaration>()

        for declaration in declarations do
            result.Add(declaration)

            if
                declaration.Kind = Function
                || declaration.Kind = Member
                || declaration.Kind = Constructor
            then
                let parameterText =
                    if declaration.Kind = Constructor then
                        constructorParameterText declaration.Text declaration.Name
                    else
                        declaration.Text

                let parameterNames = parseParameterInfos parameterText declaration.Name

                for parameter in parameterNames do
                    let parameterLine = declaration.Location.StartLine + parameter.Line - 1

                    let parameterDeclaration =
                        makeDeclaration
                            source
                            parameter.Name
                            Parameter
                            parameterLine
                            parameter.Column
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
                            (if parameter.IsBoolean then
                                 sprintf "%s: bool" parameter.Name
                             else
                                 parameter.Name)

                    result.Add(
                        { parameterDeclaration with
                            Location = sourceLocation source parameterLine parameterLine parameter.Column }
                    )

        result |> Seq.toList

    let private applyParents declarations =
        declarations
        |> List.map (fun declaration ->
            match declaration.Kind with
            | UnionCase
            | Field
            | Parameter
            | Constructor -> declaration
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

    let private applyCompilerTypeShapes (facts: SyntaxModel.Facts) (declarations: Declaration list) =
        declarations
        |> List.map (fun declaration ->
            if declaration.Kind <> Type then
                declaration
            else
                facts.Declarations
                |> List.tryFind (fun fact ->
                    fact.Name = declaration.Name
                    && ((fact.Location.StartLine = declaration.Location.StartLine)
                        || (fact.Location.StartLine <= declaration.Location.StartLine
                            && declaration.Location.StartLine <= fact.Location.EndLine))
                    && match fact.Kind with
                       | SyntaxModel.TypeFact _ -> true
                       | _ -> false)
                |> Option.map (fun fact ->
                    let shape =
                        match fact.Kind with
                        | SyntaxModel.TypeFact value -> value
                        | _ -> declaration.TypeShape

                    { declaration with
                        Location = fact.Location
                        ScopeStartLine = fact.Location.StartLine
                        ScopeEndLine = fact.Location.EndLine
                        BodyStartLine = fact.Location.StartLine
                        BodyEndLine = fact.Location.EndLine
                        IsRecord = shape = RecordType
                        IsUnion = shape = UnionType
                        IsClassLike = shape = ClassType
                        IsInterface = shape = InterfaceType
                        TypeShape = shape })
                |> Option.defaultValue declaration)

    let private addCompilerBindings source (facts: SyntaxModel.Facts) (declarations: Declaration list) =
        let result = ResizeArray<Declaration>(declarations :> seq<Declaration>)

        for fact in facts.Declarations do
            match fact.Kind with
            | SyntaxModel.BindingFact when
                result
                |> Seq.exists (fun declaration ->
                    declaration.Name = fact.Name
                    && ((declaration.Location.StartLine = fact.Location.StartLine)
                        || (fact.Location.StartLine <= declaration.Location.StartLine
                            && declaration.Location.StartLine <= fact.Location.EndLine))
                    && (declaration.Kind = Function || declaration.Kind = Value))
                |> not
                ->
                let parent = nearestParent (result |> Seq.toList) fact.Location.StartLine
                let isFunction = fact.ParameterCount > 0

                result.Add(
                    makeDeclaration
                        source
                        fact.Name
                        (if isFunction then Function else Value)
                        fact.Location.StartLine
                        fact.Location.StartColumn
                        (parent |> Option.map (fun item -> item.Name))
                        (parent |> Option.map (fun item -> item.Kind))
                        ""
                        fact.IsMutable
                        false
                        (isLiteralDeclaration source.Lines fact.Location.StartLine)
                        false
                        false
                        false
                        isFunction
                        (parent |> Option.forall (fun item -> item.Kind <> Type))
                        fact.ParameterCount
                        fact.Location.StartLine
                        fact.Location.EndLine
                        fact.Location.StartLine
                        fact.Location.EndLine
                        (sourceText source fact.Location.StartLine fact.Location.EndLine)
                )
            | _ -> ()

        result |> Seq.toList

    let private addCompilerParameters source (facts: SyntaxModel.Facts) (declarations: Declaration list) =
        let result = ResizeArray<Declaration>(declarations :> seq<Declaration>)

        for fact in facts.Declarations do
            match fact.Kind with
            | SyntaxModel.BindingFact ->
                let owner =
                    result
                    |> Seq.tryFind (fun declaration ->
                        declaration.Name = fact.Name
                        && declaration.Location.StartLine = fact.Location.StartLine
                        && (declaration.Kind = Function || declaration.Kind = Value))

                for name, parameterLocation in fact.Parameters do
                    let alreadyModeled =
                        result
                        |> Seq.exists (fun declaration ->
                            declaration.Kind = Parameter
                            && declaration.Name = name
                            && declaration.Parent = Some fact.Name
                            && declaration.Location.StartLine = parameterLocation.StartLine)

                    match owner with
                    | Some declaration when not alreadyModeled ->
                        let parameter =
                            makeDeclaration
                                source
                                name
                                Parameter
                                parameterLocation.StartLine
                                parameterLocation.StartColumn
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
                                (sourceText source parameterLocation.StartLine parameterLocation.EndLine)

                        result.Add(
                            { parameter with
                                Location = parameterLocation }
                        )
                    | _ -> ()
            | _ -> ()

        result |> Seq.toList

    let analyze (source: SourceFile) (parsedInput: ParsedInput) =
        let tokens = Scanner.scan source
        let syntaxFacts = SyntaxModel.normalize source.FullPath parsedInput

        let baseDeclarations =
            buildBaseDeclarations source
            |> applyCompilerTypeShapes syntaxFacts
            |> addCompilerBindings source syntaxFacts

        let withConstructors = addConstructors source baseDeclarations
        let withCases = addUnionCases source withConstructors
        let withFields = addFields source withCases

        let withParameters =
            addParameters source withFields |> addCompilerParameters source syntaxFacts

        let declarations = applyParents withParameters

        let referenceCounts =
            declarations
            |> List.map (fun declaration -> declaration.Name, tokenCount tokens declaration.Name 1 source.Lines.Length)
            |> Map.ofList

        let referenceCountsByDeclaration =
            declarations
            |> List.map (fun declaration ->
                (declaration.Name, declaration.Location.StartLine),
                referenceCountFor tokens declarations declaration source.Lines.Length)
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
            measuredDeclarations
            |> List.map (fun declaration ->
                (declaration.Name, declaration.Location.StartLine),
                nPath tokens declaration.BodyStartLine declaration.BodyEndLine)
            |> Map.ofList

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
          ReferenceCountsByDeclaration = referenceCountsByDeclaration
          MutatedNames = mutatedNames
          TypeFields = typeFields
          TypeMethods = typeMethods
          Expressions = syntaxFacts.Expressions
          LexicalScopes = syntaxFacts.LexicalScopes
          SyntacticReferences = syntaxFacts.References }
