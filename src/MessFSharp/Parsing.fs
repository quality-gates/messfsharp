namespace MessFSharp

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
module Parsing =
    let private checker = lazy (FSharpChecker.Create(keepAssemblyContents = false))

    let private diagnosticLocation path (diagnostic: FSharpDiagnostic) =
        { File = path
          StartLine = max 1 diagnostic.StartLine
          StartColumn = max 1 (diagnostic.StartColumn + 1)
          EndLine = max 1 diagnostic.EndLine
          EndColumn = max 1 (diagnostic.EndColumn + 1) }

    let private invalidScriptModules (source: SourceFile) tree =
        match source.Kind, tree with
        | Script, ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            modules
            |> List.choose (fun (SynModuleOrNamespace(kind = kind; range = range)) ->
                if kind = SynModuleOrNamespaceKind.NamedModule then
                    Some
                        { File = Some source.FullPath
                          Location =
                            Some
                                { File = source.FullPath
                                  StartLine = range.StartLine
                                  StartColumn = range.StartColumn + 1
                                  EndLine = range.StartLine
                                  EndColumn = range.StartColumn + 7 }
                          Message = "A top-level module in a script must use '=' and an indented body." }
                else
                    None)
        | _ -> []

    let parse (source: SourceFile) =
        try
            let parsePath, options = CompilerContext.parsingArguments source

            let results =
                checker.Value.ParseFile(
                    parsePath,
                    SourceText.ofString (Preprocessor.compilerDefaultParserText source),
                    options
                )
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
                match invalidScriptModules source results.ParseTree with
                | [] -> Ok results.ParseTree
                | errors -> Error errors
        with ex ->
            Error
                [ { File = Some source.FullPath
                    Location = None
                    Message = sprintf "Could not parse source: %s" ex.Message } ]
