namespace MessFSharp

open System
open System.Collections.Generic
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
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

    let scan (source: SourceFile) =
        let tokens = ResizeArray<SyntaxToken>()
        let text = source.Text
        let length = text.Length
        let mutable index = 0
        let mutable line = 1
        let mutable column = 1
        let mutable blockCommentDepth = 0

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
            elif text[index] = '\n' || text[index] = '\r' || Char.IsWhiteSpace(text[index]) then
                advance ()
            else
                let startLine, startColumn = startPosition ()
                let character = text[index]

                if character = '\'' && index + 2 < length && text[index + 2] = '\'' then
                    let start = index
                    advanceMany 3

                    addToken
                        tokens
                        CharacterLiteral
                        (text.Substring(start, index - start))
                        startLine
                        startColumn
                        line
                        column
                elif character = '`' then
                    advance ()
                    let start = index

                    while index < length && text[index] <> '`' do
                        advance ()

                    let value = text.Substring(start, index - start)

                    if index < length then
                        advance ()

                    addToken tokens Identifier value startLine startColumn line column
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
