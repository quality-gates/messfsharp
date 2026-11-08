namespace MessFSharp

open System
open System.IO
open System.Text.RegularExpressions
open FSharp.Compiler.Tokenization
open Domain

/// Conditional-compilation awareness. The F# parser drops declarations that are
/// in an inactive `#if` or `#else` branch, so the line-based part of the model
/// must drop them too. The compiler lexer, which marks the skipped regions as
/// inactive code, does the evaluation of the directives.
module Preprocessor =
    let private undefinedDefaultSymbol = "NOT_SET"

    let private editingIdentifier =
        Regex(
            "(?<![\\p{L}\\p{Nl}\\p{Nd}\\p{Pc}\\p{Mn}\\p{Mc}\\p{Cf}'])EDITING(?![\\p{L}\\p{Nl}\\p{Nd}\\p{Pc}\\p{Mn}\\p{Mc}\\p{Cf}'])",
            RegexOptions.CultureInvariant
        )

    let private conditionStart (tokenizer: FSharpSourceTokenizer) (state: FSharpTokenizerLexState ref) (line: string) =
        let lineTokenizer = tokenizer.CreateLineTokenizer(line)
        let mutable scanning = true
        let mutable start = None

        while scanning do
            match lineTokenizer.ScanToken(state.Value) with
            | Some token, nextState ->
                if token.TokenName = "HASH_IF" then
                    let directive =
                        line.Substring(token.LeftColumn, token.RightColumn - token.LeftColumn + 1)

                    if directive = "#if" || directive = "#elif" then
                        start <- Some(token.RightColumn + 1)

                state.Value <- nextState
            | None, nextState ->
                state.Value <- nextState
                scanning <- false

        start

    let private conditionEnd (line: string) start =
        [ line.IndexOf("//", start, StringComparison.Ordinal)
          line.IndexOf("(*", start, StringComparison.Ordinal)
          line.IndexOf('"', start)
          line.Length ]
        |> List.filter (fun index -> index >= 0)
        |> List.min

    let private nextLineOffset (characters: char array) textLength offset (line: string) =
        let lineEnd = offset + line.Length

        let afterCarriageReturn =
            if lineEnd < textLength && characters.[lineEnd] = '\r' then
                lineEnd + 1
            else
                lineEnd

        if afterCarriageReturn < textLength && characters.[afterCarriageReturn] = '\n' then
            afterCarriageReturn + 1
        else
            afterCarriageReturn

    let internal compilerDefaultParserText (source: SourceFile) =
        if not (source.Text.Contains("EDITING", StringComparison.Ordinal)) then
            source.Text
        else
            // FCS's editing parser defines EDITING even when the compiler does not.
            let tokenizer = CompilerContext.tokenizer source
            let state = ref FSharpTokenizerLexState.Initial
            let characters = source.Text.ToCharArray()
            let textLength = characters.Length
            use reader = new StringReader(source.Text)
            let mutable line = reader.ReadLine()
            let mutable offset = 0
            let mutable changed = false

            while not (isNull line) do
                match conditionStart tokenizer state line with
                | Some start ->
                    let length = conditionEnd line start - start
                    let expression = line.Substring(start, length)

                    for symbol in editingIdentifier.Matches(expression) do
                        undefinedDefaultSymbol.CopyTo(0, characters, offset + start + symbol.Index, 7)
                        changed <- true
                | None -> ()

                offset <- nextLineOffset characters textLength offset line
                line <- reader.ReadLine()

            if changed then String(characters) else source.Text

    let private hasConditionalDirective (source: SourceFile) =
        source.Lines
        |> Array.exists (fun line -> line.TrimStart().StartsWith("#if", StringComparison.Ordinal))

    /// Scans one line and tells if the compiler skipped it. The lexer state
    /// carries the directive nesting from each line to the next.
    let private scanLine (tokenizer: FSharpSourceTokenizer) (state: FSharpTokenizerLexState ref) (line: string) =
        let lineTokenizer = tokenizer.CreateLineTokenizer(line)
        let mutable scanning = true
        let mutable isInactive = false

        while scanning do
            match lineTokenizer.ScanToken(state.Value) with
            | Some token, nextState ->
                if token.ColorClass = FSharpTokenColorKind.InactiveCode then
                    isInactive <- true

                state.Value <- nextState
            | None, nextState ->
                state.Value <- nextState
                scanning <- false

        isInactive

    /// The 1-based line numbers that the compiler skipped.
    let inactiveLines (source: SourceFile) =
        if not (hasConditionalDirective source) then
            Set.empty
        else
            let tokenizer = CompilerContext.tokenizer source
            let state = ref FSharpTokenizerLexState.Initial

            source.Lines
            |> Array.mapi (fun index line -> index + 1, scanLine tokenizer state line)
            |> Array.filter snd
            |> Array.map fst
            |> Set.ofArray

    /// The same source with each inactive line made empty. The line numbering
    /// stays the same, so locations that are reported against the masked source
    /// stay correct.
    let maskInactiveLines (source: SourceFile) =
        let inactive = inactiveLines source

        if Set.isEmpty inactive then
            source
        else
            let lines =
                source.Lines
                |> Array.mapi (fun index line -> if inactive.Contains(index + 1) then "" else line)

            { source with
                Lines = lines
                Text = String.concat "\n" lines }
