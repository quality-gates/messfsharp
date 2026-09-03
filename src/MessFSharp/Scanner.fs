namespace MessFSharp

open System
open System.Collections.Generic
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CountInLoopExpression")>]
module Scanner =
    let private keywords =
        set
            [ "abstract"
              "and"
              "as"
              "assert"
              "base"
              "begin"
              "class"
              "default"
              "delegate"
              "do"
              "done"
              "downcast"
              "downto"
              "elif"
              "else"
              "end"
              "exception"
              "extern"
              "false"
              "finally"
              "for"
              "fun"
              "function"
              "if"
              "in"
              "inherit"
              "inline"
              "interface"
              "internal"
              "lazy"
              "let"
              "match"
              "member"
              "module"
              "mutable"
              "namespace"
              "new"
              "null"
              "of"
              "open"
              "or"
              "override"
              "private"
              "public"
              "rec"
              "return"
              "static"
              "struct"
              "then"
              "to"
              "true"
              "try"
              "type"
              "upcast"
              "use"
              "val"
              "void"
              "when"
              "while"
              "with"
              "yield"
              "yield!"
              "use!"
              "let!"
              "do!"
              "return!" ]

    let private isIdentifierStart character =
        Char.IsLetter(character) || character = '_' || character = '\''

    let private isIdentifierPart character =
        Char.IsLetterOrDigit(character) || character = '_' || character = '\''

    let private isOperatorCharacter (character: char) =
        "!%&*+-./<=>?@^|~:".IndexOf(character) >= 0

    let private addToken (tokens: ResizeArray<SyntaxToken>) kind text line column endLine endColumn =
        tokens.Add(
            { Text = text
              Kind = kind
              Line = line
              Column = column
              EndLine = endLine
              EndColumn = endColumn }
        )

    [<NoEquality; NoComparison>]
    type private InterpolationState =
        { Terminator: string
          IsVerbatim: bool
          DollarCount: int
          mutable BraceDepth: int }

    let scan (source: SourceFile) =
        let tokens = ResizeArray<SyntaxToken>()
        let text = source.Text
        let length = text.Length
        let mutable index = 0
        let mutable line = 1
        let mutable column = 1
        let mutable blockCommentDepth = 0
        let activeInterpolations = System.Collections.Generic.Stack<InterpolationState>()
        let mutable interpolationSegmentStart = -1
        let mutable interpolationSegmentStartLine = 1
        let mutable interpolationSegmentStartColumn = 1

        let advance () =
            if index < length then
                if text[index] = '\n' then
                    line <- line + 1
                    column <- 1
                else
                    column <- column + 1

                index <- index + 1

        let advanceMany count =
            for _ in 1..count do
                advance ()

        let startPosition () = line, column

        while index < length do
            if blockCommentDepth > 0 then
                if index + 1 < length && text[index] = '(' && text[index + 1] = '*' then
                    blockCommentDepth <- blockCommentDepth + 1
                    advanceMany 2
                elif index + 1 < length && text[index] = '*' && text[index + 1] = ')' then
                    blockCommentDepth <- blockCommentDepth - 1
                    advanceMany 2
                else
                    advance ()
            elif index + 1 < length && text[index] = '(' && text[index + 1] = '*' then
                blockCommentDepth <- 1
                advanceMany 2
            elif text[index] = '/' && index + 1 < length && text[index + 1] = '/' then
                while index < length && text[index] <> '\n' do
                    advance ()
            elif activeInterpolations.Count > 0 && activeInterpolations.Peek().BraceDepth = 0 then
                let state = activeInterpolations.Peek()
                let startLine, startColumn =
                    if interpolationSegmentStart >= 0 then
                        interpolationSegmentStartLine, interpolationSegmentStartColumn
                    else
                        startPosition ()
                let start = if interpolationSegmentStart >= 0 then interpolationSegmentStart else index
                interpolationSegmentStart <- -1

                let mutable closed = false
                let mutable holeFound = false
                let isTriple = state.Terminator.Length = 3

                while index < length && not closed && not holeFound do
                    if index + state.Terminator.Length <= length && text.Substring(index, state.Terminator.Length) = state.Terminator then
                        advanceMany state.Terminator.Length
                        closed <- true
                    elif text[index] = '{' && index + 1 < length && text[index + 1] = '{' then
                        advanceMany 2
                    elif text[index] = '}' && index + 1 < length && text[index + 1] = '}' then
                        advanceMany 2
                    elif text[index] = '{' then
                        holeFound <- true
                    elif text[index] = '\\' && not state.IsVerbatim && not isTriple && index + 1 < length then
                        advanceMany 2
                    elif text[index] = '"' && state.IsVerbatim && not isTriple && index + 1 < length && text[index + 1] = '"' then
                        advanceMany 2
                    else
                        advance ()

                if index > start then
                    addToken tokens StringLiteral (text.Substring(start, index - start)) startLine startColumn line column

                if closed then
                    activeInterpolations.Pop() |> ignore
                elif holeFound then
                    let holeLine, holeColumn = startPosition ()
                    advance ()
                    addToken tokens Punctuation "{" holeLine holeColumn line column
                    state.BraceDepth <- 1
            elif text[index] = '\n' || text[index] = '\r' || Char.IsWhiteSpace(text[index]) then
                advance ()
            else
                let startLine, startColumn = startPosition ()
                let character = text[index]

                if character = '\'' && index + 3 < length && text[index + 1] = '\\' && text[index + 3] = '\'' then
                    let start = index
                    advanceMany 4
                    addToken tokens CharacterLiteral (text.Substring(start, index - start)) startLine startColumn line column
                elif character = '\'' && index + 2 < length && text[index + 2] = '\'' then
                    let start = index
                    advanceMany 3
                    addToken tokens CharacterLiteral (text.Substring(start, index - start)) startLine startColumn line column
                elif character = '`' && index + 1 < length && text[index + 1] = '`' then
                    advanceMany 2
                    let start = index
                    while index + 1 < length && not (text[index] = '`' && text[index + 1] = '`') do
                        advance ()
                    let value = text.Substring(start, index - start)
                    if index + 1 < length && text[index] = '`' && text[index + 1] = '`' then
                        advanceMany 2
                    addToken tokens Identifier value startLine startColumn line column
                elif character = '`' then
                    advance ()
                    let start = index

                    while index < length && text[index] <> '`' do
                        advance ()

                    let value = text.Substring(start, index - start)

                    if index < length then
                        advance ()

                    addToken tokens Identifier value startLine startColumn line column
                elif character = '$'
                     && index + 1 < length
                     && (text[index + 1] = '"' || (text[index + 1] = '@' && index + 2 < length && text[index + 2] = '"')) then
                    let isVerbatim = text[index + 1] = '@'
                    let quoteIndex = if isVerbatim then index + 2 else index + 1
                    let isTriple = quoteIndex + 2 < length && text[quoteIndex + 1] = '"' && text[quoteIndex + 2] = '"'
                    let terminator = if isTriple then "\"\"\"" else "\""
                    let prefixLength = (if isVerbatim then 2 else 1) + terminator.Length
                    interpolationSegmentStart <- index
                    interpolationSegmentStartLine <- startLine
                    interpolationSegmentStartColumn <- startColumn
                    advanceMany prefixLength
                    let state = { Terminator = terminator; IsVerbatim = isVerbatim; DollarCount = 1; BraceDepth = 0 }
                    activeInterpolations.Push(state)
                elif character = '@'
                     && index + 2 < length
                     && text[index + 1] = '$'
                     && text[index + 2] = '"' then
                    let quoteIndex = index + 2
                    let isTriple = quoteIndex + 2 < length && text[quoteIndex + 1] = '"' && text[quoteIndex + 2] = '"'
                    let terminator = if isTriple then "\"\"\"" else "\""
                    let prefixLength = 2 + terminator.Length
                    interpolationSegmentStart <- index
                    interpolationSegmentStartLine <- startLine
                    interpolationSegmentStartColumn <- startColumn
                    advanceMany prefixLength
                    let state = { Terminator = terminator; IsVerbatim = true; DollarCount = 1; BraceDepth = 0 }
                    activeInterpolations.Push(state)
                elif character = '@' && index + 1 < length && text[index + 1] = '"' then
                    let isTriple = index + 3 < length && text[index + 2] = '"' && text[index + 3] = '"'
                    let terminator = if isTriple then "\"\"\"" else "\""
                    let start = index
                    advanceMany (1 + terminator.Length)
                    let mutable closed = false
                    while index < length && not closed do
                        if index + terminator.Length <= length && text.Substring(index, terminator.Length) = terminator then
                            advanceMany terminator.Length
                            closed <- true
                        elif not isTriple && text[index] = '"' && index + 1 < length && text[index + 1] = '"' then
                            advanceMany 2
                        else
                            advance ()
                    addToken tokens StringLiteral (text.Substring(start, index - start)) startLine startColumn line column
                elif character = '"' then
                    let triple = index + 2 < length && text[index + 1] = '"' && text[index + 2] = '"'
                    let terminator = if triple then "\"\"\"" else "\""
                    let start = index
                    advanceMany terminator.Length
                    let mutable closed = false

                    while index < length && not closed do
                        if
                            index + terminator.Length <= length
                            && text.Substring(index, terminator.Length) = terminator
                        then
                            advanceMany terminator.Length
                            closed <- true
                        elif text[index] = '\\' && not triple && index + 1 < length then
                            advanceMany 2
                        else
                            advance ()

                    addToken
                        tokens
                        StringLiteral
                        (text.Substring(start, index - start))
                        startLine
                        startColumn
                        line
                        column
                elif character = '{' then
                    advance ()
                    if activeInterpolations.Count > 0 then
                        let state = activeInterpolations.Peek()
                        state.BraceDepth <- state.BraceDepth + 1
                    addToken tokens Punctuation "{" startLine startColumn line column
                elif character = '}' then
                    advance ()
                    if activeInterpolations.Count > 0 then
                        let state = activeInterpolations.Peek()
                        state.BraceDepth <- state.BraceDepth - 1
                        if state.BraceDepth = 0 then
                            interpolationSegmentStart <- index
                            interpolationSegmentStartLine <- line
                            interpolationSegmentStartColumn <- column
                    addToken tokens Punctuation "}" startLine startColumn line column
                elif
                    character = '\''
                    && index + 1 < length
                    && (Char.IsLetter(text[index + 1]) || text[index + 1] = '_')
                then
                    let start = index
                    advance ()

                    while index < length && isIdentifierPart text[index] do
                        advance ()

                    addToken tokens Identifier (text.Substring(start, index - start)) startLine startColumn line column
                elif isIdentifierStart character then
                    let start = index
                    advance ()

                    while index < length && isIdentifierPart text[index] do
                        advance ()

                    let value = text.Substring(start, index - start)
                    let kind = if keywords.Contains(value) then Keyword else Identifier
                    addToken tokens kind value startLine startColumn line column
                elif Char.IsDigit character then
                    let start = index
                    advance ()

                    while index < length
                          && (Char.IsLetterOrDigit(text[index]) || text[index] = '.' || text[index] = '_') do
                        advance ()

                    addToken tokens Number (text.Substring(start, index - start)) startLine startColumn line column
                elif isOperatorCharacter character then
                    let start = index
                    advance ()

                    while index < length && isOperatorCharacter text[index] do
                        advance ()

                    addToken tokens Operator (text.Substring(start, index - start)) startLine startColumn line column
                else
                    advance ()

                    let kind =
                        if "()[]{};,".IndexOf(character) >= 0 then
                            Punctuation
                        else
                            Operator

                    addToken tokens kind (string character) startLine startColumn line column

        tokens.ToArray()
