namespace MessFSharp

open System
open FSharp.Compiler.Tokenization
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CountInLoopExpression")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyMethods")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "GlobalVariable")>]
module Scanner =
    [<NoEquality; NoComparison>]
    type private LexedToken =
        { Line: int
          LeftColumn: int
          Text: string
          Kind: SyntaxTokenKind }

    let private operatorCharacters = "!%&*+-./<=>?@^|~:"

    let private punctuationCharacters = "()[]{};,"

    let private isOperatorCharacter (character: char) =
        operatorCharacters.IndexOf(character) >= 0

    let private isPunctuationCharacter (character: char) =
        punctuationCharacters.IndexOf(character) >= 0

    let private isOperatorText (text: string) =
        not (String.IsNullOrEmpty text) && text |> Seq.forall isOperatorCharacter

    let private tokenText (line: string) (token: FSharpTokenInfo) =
        if String.IsNullOrEmpty line then
            ""
        else
            let left = max 0 token.LeftColumn
            let right = min (line.Length - 1) token.RightColumn

            if right < left then
                ""
            else
                line.Substring(left, right - left + 1)

    let private fallbackKind (text: string) =
        if String.IsNullOrWhiteSpace text then
            None
        elif isOperatorText text then
            Some Operator
        elif text |> Seq.forall isPunctuationCharacter then
            Some Punctuation
        else
            Some Identifier

    let private kindOf (token: FSharpTokenInfo) text =
        match token.ColorClass with
        | FSharpTokenColorKind.Comment
        | FSharpTokenColorKind.InactiveCode -> None
        | FSharpTokenColorKind.String ->
            if String.Equals(token.TokenName, "CHAR", StringComparison.Ordinal) then
                Some CharacterLiteral
            else
                Some StringLiteral
        | FSharpTokenColorKind.Number -> Some Number
        | FSharpTokenColorKind.Keyword
        | FSharpTokenColorKind.PreprocessorKeyword -> if isOperatorText text then Some Operator else Some Keyword
        | FSharpTokenColorKind.Identifier
        | FSharpTokenColorKind.UpperIdentifier -> Some Identifier
        | FSharpTokenColorKind.Operator -> Some Operator
        | FSharpTokenColorKind.Punctuation ->
            if isOperatorText text then
                Some Operator
            else
                Some Punctuation
        | FSharpTokenColorKind.Default
        | FSharpTokenColorKind.Text ->
            if String.Equals(token.TokenName, "WHITESPACE", StringComparison.Ordinal) then
                None
            else
                fallbackKind text
        | _ -> fallbackKind text

    let private scanCompilerLine
        (tokenizer: FSharpSourceTokenizer)
        (state: FSharpTokenizerLexState ref)
        (line: string)
        (handleToken: FSharpTokenInfo -> unit)
        =
        let lineTokenizer = tokenizer.CreateLineTokenizer(line)
        let mutable scanning = true

        while scanning do
            match lineTokenizer.ScanToken(state.Value) with
            | Some token, nextState ->
                state.Value <- nextState
                handleToken token
            | None, nextState ->
                state.Value <- nextState
                scanning <- false

    let private compilerNonCodeRanges
        (tokenizer: FSharpSourceTokenizer)
        (state: FSharpTokenizerLexState ref)
        (line: string)
        =
        let ranges = ResizeArray<int * int>()

        scanCompilerLine tokenizer state line (fun token ->
            match token.ColorClass with
            | FSharpTokenColorKind.Comment
            | FSharpTokenColorKind.String
            | FSharpTokenColorKind.InactiveCode -> ranges.Add(token.LeftColumn, token.RightColumn + 1)
            | _ -> ())

        ranges.ToArray()

    let private lex (source: SourceFile) =
        let tokenizer = FSharpSourceTokenizer([], Some source.FullPath, None, None)
        let state = ref FSharpTokenizerLexState.Initial
        let tokens = ResizeArray<LexedToken>()

        for lineIndex in 0 .. source.Lines.Length - 1 do
            let line = source.Lines[lineIndex]

            scanCompilerLine tokenizer state line (fun token ->
                let text = tokenText line token

                if not (String.IsNullOrEmpty text) then
                    match kindOf token text with
                    | Some kind ->
                        tokens.Add(
                            { Line = lineIndex + 1
                              LeftColumn = token.LeftColumn
                              Text = text
                              Kind = kind }
                        )
                    | None -> ())

        tokens.ToArray()

    let private adjacent (left: LexedToken) (right: LexedToken) =
        left.Line = right.Line && right.LeftColumn = left.LeftColumn + left.Text.Length

    let private isOrdered (tokens: LexedToken array) =
        let mutable ordered = true

        for index in 1 .. tokens.Length - 1 do
            let previous = tokens[index - 1]
            let current = tokens[index]

            if
                current.Line < previous.Line
                || (current.Line = previous.Line && current.LeftColumn < previous.LeftColumn)
            then
                ordered <- false

        ordered

    let private mergeStringTokens (tokens: LexedToken array) =
        let result = ResizeArray<LexedToken>()

        let ordered =
            if isOrdered tokens then
                tokens
            else
                tokens
                |> Array.sortWith (fun left right ->
                    let lineComparison = compare left.Line right.Line

                    if lineComparison <> 0 then
                        lineComparison
                    else
                        compare left.LeftColumn right.LeftColumn)

        for token in ordered do
            if
                result.Count > 0
                && token.Kind = StringLiteral
                && result[result.Count - 1].Kind = StringLiteral
                && adjacent result[result.Count - 1] token
            then
                let previous = result[result.Count - 1]

                result[result.Count - 1] <-
                    { previous with
                        Text = previous.Text + token.Text }
            else
                result.Add(token)

        result.ToArray()

    let private splitCompositePunctuation (token: LexedToken) =
        if
            token.Kind = Punctuation
            && token.Text.Length > 1
            && token.Text |> Seq.exists isPunctuationCharacter
        then
            token.Text.ToCharArray()
            |> Array.mapi (fun offset character ->
                { token with
                    LeftColumn = token.LeftColumn + offset
                    Text = string character
                    Kind =
                        if isOperatorCharacter character then
                            Operator
                        else
                            Punctuation })
        else
            [| token |]

    let private splitPunctuation (tokens: LexedToken array) =
        tokens |> Array.collect splitCompositePunctuation

    let private isIdentifierStart (character: char) =
        Char.IsLetter character
        || character = '_'
        || character = '\''
        || character = '`'

    let private part (token: LexedToken) offset length (kind: SyntaxTokenKind) =
        { token with
            LeftColumn = token.LeftColumn + offset
            Text = token.Text.Substring(offset, length)
            Kind = kind }

    let private splitNumericToken (token: LexedToken) (nextToken: LexedToken option) =
        if token.Kind <> Number then
            [| token |]
        else
            let text = token.Text
            let rangeIndex = text.IndexOf("..", StringComparison.Ordinal)

            if rangeIndex > 0 && rangeIndex = text.Length - 2 then
                [| part token 0 rangeIndex Number; part token rangeIndex 2 Operator |]
            else
                let dotIndex = text.IndexOf('.')

                if
                    dotIndex > 0
                    && dotIndex < text.Length - 1
                    && text.IndexOf('.', dotIndex + 1) < 0
                    && isIdentifierStart text[dotIndex + 1]
                then
                    [| part token 0 dotIndex Number
                       part token dotIndex 1 Operator
                       part token (dotIndex + 1) (text.Length - dotIndex - 1) Identifier |]
                elif
                    dotIndex = text.Length - 1
                    && dotIndex > 0
                    && (match nextToken with
                        | Some next -> next.Kind = Identifier && adjacent token next
                        | None -> false)
                then
                    [| part token 0 dotIndex Number; part token dotIndex 1 Operator |]
                else
                    [| token |]

    let private splitNumericTokens (tokens: LexedToken array) =
        let result = ResizeArray<LexedToken>()

        for index in 0 .. tokens.Length - 1 do
            let nextToken =
                if index + 1 < tokens.Length then
                    Some tokens[index + 1]
                else
                    None

            result.AddRange(splitNumericToken tokens[index] nextToken)

        result.ToArray()

    let private mergeQuotedIdentifiers (tokens: LexedToken array) =
        let result = ResizeArray<LexedToken>()
        let mutable index = 0

        while index < tokens.Length do
            if tokens[index].Kind = Identifier && tokens[index].Text = "`" then
                let mutable closing = index + 1

                while closing < tokens.Length
                      && tokens[closing].Line = tokens[index].Line
                      && adjacent tokens[closing - 1] tokens[closing]
                      && (tokens[closing].Kind <> Identifier || tokens[closing].Text <> "`") do
                    closing <- closing + 1

                if
                    closing < tokens.Length
                    && tokens[closing].Kind = Identifier
                    && tokens[closing].Text = "`"
                    && adjacent tokens[closing - 1] tokens[closing]
                then
                    let text =
                        tokens[index..closing]
                        |> Array.map (fun token -> token.Text)
                        |> String.concat ""

                    result.Add({ tokens[index] with Text = text })

                    index <- closing + 1
                else
                    result.Add(tokens[index])
                    index <- index + 1
            else
                result.Add(tokens[index])
                index <- index + 1

        result.ToArray()

    let private lexFragment (lineNumber: int) (offset: int) (text: string) =
        let tokenizer = FSharpSourceTokenizer([], Some "<interpolation>", None, None)
        let state = ref FSharpTokenizerLexState.Initial
        let tokens = ResizeArray<LexedToken>()

        scanCompilerLine tokenizer state text (fun token ->
            let tokenText = tokenText text token

            if not (String.IsNullOrEmpty tokenText) then
                match kindOf token tokenText with
                | Some kind ->
                    tokens.Add(
                        { Line = lineNumber
                          LeftColumn = offset + token.LeftColumn
                          Text = tokenText
                          Kind = kind }
                    )
                | None -> ())

        tokens.ToArray()

    let private runLength (line: string) character startIndex limit =
        let mutable length = 0

        while startIndex + length < limit && line[startIndex + length] = character do
            length <- length + 1

        length

    let private findExtendedInterpolationEnd (line: string) contentStart (terminator: string) isVerbatim dollarCount =
        let tokenizer = FSharpSourceTokenizer([], Some "<interpolation>", None, None)
        let state = ref FSharpTokenizerLexState.Initial

        let nonCodeRanges =
            line.Substring(contentStart)
            |> compilerNonCodeRanges tokenizer state
            |> Array.map (fun (rangeStart, rangeEnd) -> rangeStart + contentStart, rangeEnd + contentStart)

        let nonCodeRangeEndAt column =
            nonCodeRanges
            |> Array.tryPick (fun (rangeStart, rangeEnd) ->
                if rangeStart <= column && column < rangeEnd then
                    Some rangeEnd
                else
                    None)

        let mutable cursor = contentStart
        let mutable inHole = false
        let mutable nestedBraceDepth = 0
        let mutable contentEnd = line.Length
        let mutable closed = false

        while cursor < line.Length && not closed do
            if inHole then
                match nonCodeRangeEndAt cursor with
                | Some rangeEnd -> cursor <- min line.Length rangeEnd
                | None when line[cursor] = '{' ->
                    nestedBraceDepth <- nestedBraceDepth + 1
                    cursor <- cursor + 1
                | None when line[cursor] = '}' ->
                    let closingLength = runLength line '}' cursor line.Length

                    if nestedBraceDepth = 0 && closingLength >= dollarCount then
                        inHole <- false
                        cursor <- cursor + dollarCount
                    else
                        if nestedBraceDepth > 0 then
                            nestedBraceDepth <- nestedBraceDepth - 1

                        cursor <- cursor + 1
                | None -> cursor <- cursor + 1
            else
                let openingLength = runLength line '{' cursor line.Length

                if openingLength >= dollarCount then
                    inHole <- true
                    nestedBraceDepth <- 0
                    cursor <- cursor + dollarCount
                elif not isVerbatim && line[cursor] = '\\' && cursor + 1 < line.Length then
                    cursor <- cursor + 2
                elif
                    isVerbatim
                    && terminator.Length = 1
                    && line[cursor] = '"'
                    && cursor + 1 < line.Length
                    && line[cursor + 1] = '"'
                then
                    cursor <- cursor + 2
                elif
                    cursor + terminator.Length <= line.Length
                    && line.Substring(cursor, terminator.Length) = terminator
                then
                    contentEnd <- cursor
                    closed <- true
                else
                    cursor <- cursor + 1

        contentEnd

    let private tryInterpolationPrefix (line: string) startIndex =
        let mutable cursor = startIndex
        let hasAtBefore = line[cursor] = '@'

        if hasAtBefore then
            cursor <- cursor + 1

        let dollarStart = cursor

        while cursor < line.Length && line[cursor] = '$' do
            cursor <- cursor + 1

        let dollarCount = cursor - dollarStart

        if dollarCount < 2 then
            None
        else
            let hasAtAfter = cursor < line.Length && line[cursor] = '@'

            if hasAtAfter then
                cursor <- cursor + 1

            if hasAtBefore && hasAtAfter then
                None
            elif cursor >= line.Length || line[cursor] <> '"' then
                None
            else
                let quoteStart = cursor

                let isTriple =
                    cursor + 2 < line.Length && line[cursor + 1] = '"' && line[cursor + 2] = '"'

                let quoteLength = if isTriple then 3 else 1
                let contentStart = cursor + quoteLength
                let terminator = if isTriple then "\"\"\"" else "\""
                let isVerbatim = hasAtBefore || hasAtAfter

                let contentEnd =
                    findExtendedInterpolationEnd line contentStart terminator isVerbatim dollarCount

                Some(quoteStart, contentStart, contentEnd, dollarCount)

    let private interpolationHoles (line: string) startColumn endColumn dollarCount =
        let holes = ResizeArray<int * int * int>()
        let tokenizer = FSharpSourceTokenizer([], Some "<interpolation>", None, None)
        let state = ref FSharpTokenizerLexState.Initial

        let nonCodeRanges =
            line.Substring(startColumn, endColumn - startColumn)
            |> compilerNonCodeRanges tokenizer state
            |> Array.map (fun (rangeStart, rangeEnd) -> rangeStart + startColumn, rangeEnd + startColumn)

        let nonCodeRangeEndAt column =
            nonCodeRanges
            |> Array.tryPick (fun (rangeStart, rangeEnd) ->
                if rangeStart <= column && column < rangeEnd then
                    Some rangeEnd
                else
                    None)

        let mutable cursor = startColumn

        while cursor < endColumn do
            match nonCodeRangeEndAt cursor with
            | Some rangeEnd -> cursor <- min endColumn rangeEnd
            | None when line[cursor] = '{' ->
                let openingLength = runLength line '{' cursor endColumn

                if openingLength >= dollarCount then
                    let openingStart = cursor
                    let bodyStart = cursor + dollarCount
                    let mutable bodyEnd = bodyStart
                    let mutable nestedBraceDepth = 0
                    let mutable foundEnd = false

                    while bodyEnd < endColumn && not foundEnd do
                        match nonCodeRangeEndAt bodyEnd with
                        | Some rangeEnd -> bodyEnd <- min endColumn rangeEnd
                        | None when line[bodyEnd] = '{' ->
                            nestedBraceDepth <- nestedBraceDepth + 1
                            bodyEnd <- bodyEnd + 1
                        | None when line[bodyEnd] = '}' ->
                            let closingLength = runLength line '}' bodyEnd endColumn

                            if nestedBraceDepth = 0 && closingLength >= dollarCount then
                                foundEnd <- true
                            else
                                if nestedBraceDepth > 0 then
                                    nestedBraceDepth <- nestedBraceDepth - 1

                                bodyEnd <- bodyEnd + 1
                        | None -> bodyEnd <- bodyEnd + 1

                    if foundEnd then
                        holes.Add(openingStart, bodyStart, bodyEnd)
                        cursor <- bodyEnd + dollarCount
                    else
                        cursor <- endColumn
                else
                    cursor <- cursor + max 1 openingLength
            | None -> cursor <- cursor + 1

        holes.ToArray()

    let private tokenIsInsideRange (token: LexedToken) lineNumber startColumn endColumn =
        token.Line = lineNumber
        && token.LeftColumn >= startColumn
        && token.LeftColumn + token.Text.Length <= endColumn

    let private hasStringTokenAt (tokens: LexedToken array) lineNumber column =
        tokens
        |> Array.exists (fun token ->
            token.Line = lineNumber
            && token.Kind = StringLiteral
            && token.LeftColumn <= column
            && column < token.LeftColumn + token.Text.Length)

    let private nonCodeRanges (source: SourceFile) =
        let tokenizer = FSharpSourceTokenizer([], Some source.FullPath, None, None)
        let state = ref FSharpTokenizerLexState.Initial
        let ranges = ResizeArray<int * int * int>()

        for lineIndex in 0 .. source.Lines.Length - 1 do
            let line = source.Lines[lineIndex]

            for startColumn, endColumn in compilerNonCodeRanges tokenizer state line do
                ranges.Add(lineIndex + 1, startColumn, endColumn)

        ranges.ToArray()

    let private isInsideNonCodeRange ranges lineNumber column =
        ranges
        |> Array.exists (fun (line, startColumn, endColumn) ->
            line = lineNumber && startColumn <= column && column < endColumn)

    let private containsExtendedInterpolationPrefix (source: SourceFile) =
        source.Lines
        |> Array.exists (fun line -> line.Contains("$$", StringComparison.Ordinal))

    let private supplementExtendedInterpolationTokens (source: SourceFile) (tokens: LexedToken array) =
        let result = ResizeArray<LexedToken>()
        let replacements = ResizeArray<int * int * int>()
        let supplements = ResizeArray<LexedToken>()
        let ignoredRanges = nonCodeRanges source

        for lineIndex in 0 .. source.Lines.Length - 1 do
            let line = source.Lines[lineIndex]
            let lineNumber = lineIndex + 1
            let mutable index = 0

            while index < line.Length do
                match tryInterpolationPrefix line index with
                | Some(quoteStart, contentStart, contentEnd, dollarCount) ->
                    if
                        not (isInsideNonCodeRange ignoredRanges lineNumber index)
                        && not (hasStringTokenAt tokens lineNumber index)
                    then
                        let quoteLength = contentStart - quoteStart
                        let terminator = line.Substring(quoteStart, quoteLength)

                        let fullEnd =
                            if
                                contentEnd < line.Length
                                && contentEnd + quoteLength <= line.Length
                                && line.Substring(contentEnd, quoteLength) = terminator
                            then
                                contentEnd + quoteLength
                            else
                                line.Length

                        replacements.Add(lineNumber, index, fullEnd)
                        let lineHoles = interpolationHoles line contentStart contentEnd dollarCount

                        let addStringToken startColumn endColumn =
                            if endColumn > startColumn then
                                supplements.Add(
                                    { Line = lineNumber
                                      LeftColumn = startColumn
                                      Text = line.Substring(startColumn, endColumn - startColumn)
                                      Kind = StringLiteral }
                                )

                        let addPunctuationToken character column =
                            supplements.Add(
                                { Line = lineNumber
                                  LeftColumn = column
                                  Text = string character
                                  Kind = Punctuation }
                            )

                        if lineHoles.Length = 0 then
                            addStringToken index fullEnd
                        else
                            let mutable segmentStart = index

                            for openingStart, bodyStart, closingStart in lineHoles do
                                addStringToken segmentStart openingStart

                                for offset in 0 .. dollarCount - 1 do
                                    addPunctuationToken '{' (openingStart + offset)

                                supplements.AddRange(
                                    lexFragment
                                        lineNumber
                                        bodyStart
                                        (line.Substring(bodyStart, closingStart - bodyStart))
                                )

                                for offset in 0 .. dollarCount - 1 do
                                    addPunctuationToken '}' (closingStart + offset)

                                segmentStart <- closingStart + dollarCount

                            addStringToken segmentStart fullEnd

                    index <- max (index + 1) contentEnd
                | None -> index <- index + 1

        for token in tokens do
            let isReplaced =
                replacements
                |> Seq.exists (fun (lineNumber, startColumn, endColumn) ->
                    tokenIsInsideRange token lineNumber startColumn endColumn)

            if not isReplaced then
                result.Add(token)

        for token in supplements do
            result.Add(token)

        result.ToArray()

    let private supplementInterpolationTokens (source: SourceFile) (tokens: LexedToken array) =
        if containsExtendedInterpolationPrefix source then
            supplementExtendedInterpolationTokens source tokens
        else
            tokens

    let private mergeTypeParameterQuotes (tokens: LexedToken array) =
        let result = ResizeArray<LexedToken>()
        let mutable index = 0

        while index < tokens.Length do
            if
                index + 1 < tokens.Length
                && tokens[index].Kind = Identifier
                && tokens[index].Text = "'"
                && tokens[index + 1].Kind = Identifier
                && adjacent tokens[index] tokens[index + 1]
            then
                result.Add(
                    { tokens[index] with
                        Text = tokens[index].Text + tokens[index + 1].Text }
                )

                index <- index + 2
            else
                result.Add(tokens[index])
                index <- index + 1

        result.ToArray()

    let private sortLexedTokens (tokens: LexedToken array) =
        if isOrdered tokens then
            tokens
        else
            tokens
            |> Array.sortWith (fun left right ->
                let lineComparison = compare left.Line right.Line

                if lineComparison <> 0 then
                    lineComparison
                else
                    compare left.LeftColumn right.LeftColumn)

    let private displayText (token: LexedToken) =
        if
            token.Kind = Identifier
            && token.Text.Length >= 2
            && token.Text.StartsWith("`", StringComparison.Ordinal)
            && token.Text.EndsWith("`", StringComparison.Ordinal)
        then
            let delimiterLength =
                if token.Text.StartsWith("``", StringComparison.Ordinal) then
                    2
                else
                    1

            if token.Text.Length > delimiterLength * 2 then
                token.Text.Substring(delimiterLength, token.Text.Length - delimiterLength * 2)
            else
                token.Text
        else
            token.Text

    let private toSyntaxToken (token: LexedToken) =
        { Text = displayText token
          Kind = token.Kind
          Line = token.Line
          Column = token.LeftColumn + 1
          EndLine = token.Line
          EndColumn = token.LeftColumn + token.Text.Length + 1 }

    /// Tokenizes source through the F# compiler lexer and exposes the small
    /// domain token contract used by the analyzer.
    let scan (source: SourceFile) =
        source
        |> lex
        |> mergeStringTokens
        |> supplementInterpolationTokens source
        |> mergeStringTokens
        |> splitPunctuation
        |> splitNumericTokens
        |> mergeQuotedIdentifiers
        |> mergeTypeParameterQuotes
        |> sortLexedTokens
        |> Array.map toSyntaxToken
