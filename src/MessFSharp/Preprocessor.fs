namespace MessFSharp

open System
open FSharp.Compiler.Tokenization
open Domain

/// Conditional-compilation awareness. The F# parser drops declarations that are
/// in an inactive `#if` or `#else` branch, so the line-based part of the model
/// must drop them too. The compiler lexer, which marks the skipped regions as
/// inactive code, does the evaluation of the directives.
module Preprocessor =
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
            let tokenizer = FSharpSourceTokenizer([], Some source.FullPath, None, None)
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
