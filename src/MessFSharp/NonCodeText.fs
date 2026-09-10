namespace MessFSharp

open FSharp.Compiler.Tokenization
open Domain

/// Multiline strings and block comments hold text that looks like F# code but
/// is not code. The line-based part of the model must not make declarations
/// from that text. The compiler lexer, which colors each token, finds the
/// boundaries.
module NonCodeText =
    let private isNonCodeToken (token: FSharpTokenInfo) =
        match token.ColorClass with
        | FSharpTokenColorKind.Comment
        | FSharpTokenColorKind.String
        | FSharpTokenColorKind.InactiveCode -> true
        | _ -> token.TokenName = "WHITESPACE"

    /// Scans one line and tells if all of its tokens are non-code text. The
    /// lexer state carries the open string or comment from each line to the
    /// next.
    let private scanLine (tokenizer: FSharpSourceTokenizer) (state: FSharpTokenizerLexState ref) (line: string) =
        let lineTokenizer = tokenizer.CreateLineTokenizer(line)
        let mutable scanning = true
        let mutable tokenCount = 0
        let mutable codeTokenCount = 0

        while scanning do
            match lineTokenizer.ScanToken(state.Value) with
            | Some token, nextState ->
                tokenCount <- tokenCount + 1

                if not (isNonCodeToken token) then
                    codeTokenCount <- codeTokenCount + 1

                state.Value <- nextState
            | None, nextState ->
                state.Value <- nextState
                scanning <- false

        tokenCount > 0 && codeTokenCount = 0

    /// The 1-based line numbers that hold no F# code.
    let lines (source: SourceFile) =
        let tokenizer = FSharpSourceTokenizer([], Some source.FullPath, None, None)
        let state = ref FSharpTokenizerLexState.Initial

        source.Lines
        |> Array.mapi (fun index line -> index + 1, scanLine tokenizer state line)
        |> Array.filter snd
        |> Array.map fst
        |> Set.ofArray
