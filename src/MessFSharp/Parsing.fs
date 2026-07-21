namespace MessFSharp

open System
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open Domain

module Parsing =
    let private checker = lazy (FSharpChecker.Create(keepAssemblyContents = false))

    let private diagnosticLocation path (diagnostic: FSharpDiagnostic) =
        { File = path
          StartLine = max 1 diagnostic.StartLine
          StartColumn = max 1 (diagnostic.StartColumn + 1)
          EndLine = max 1 diagnostic.EndLine
          EndColumn = max 1 (diagnostic.EndColumn + 1) }

    let parse (source: SourceFile) =
        try
            let options =
                { FSharpParsingOptions.Default with
                    SourceFiles = [| source.FullPath |]
                    IsInteractive = source.Kind = Script }

            let results =
                checker.Value.ParseFile(source.FullPath, SourceText.ofString source.Text, options)
                |> Async.RunSynchronously

            let errors =
                results.Diagnostics
                |> Array.filter (fun diagnostic -> diagnostic.Severity.ToString() = "Error")
                |> Array.map (fun diagnostic ->
                    { File = Some source.FullPath
                      Location = Some(diagnosticLocation source.FullPath diagnostic)
                      Message = diagnostic.Message })
                |> Array.toList

            if results.ParseHadErrors then
                if List.isEmpty errors then
                    Error
                        [ { File = Some source.FullPath
                            Location = None
                            Message = "F# parser reported an error without a diagnostic." } ]
                else
                    Error errors
            else
                Ok()
        with ex ->
            Error
                [ { File = Some source.FullPath
                    Location = None
                    Message = sprintf "Could not parse source: %s" ex.Message } ]
