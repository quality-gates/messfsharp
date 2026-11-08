namespace MessFSharp

open System
open FSharp.Compiler
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Tokenization
open Domain

module internal CompilerContext =
    let parsingArguments (source: SourceFile) =
        let parsePath =
            match IO.Path.GetExtension(source.FullPath).ToLowerInvariant() with
            | ".fs"
            | ".fsi"
            | ".fsx" -> source.FullPath
            | _ ->
                source.FullPath
                + if source.Kind = Script then ".fsx"
                  elif source.Kind = Signature then ".fsi"
                  else ".fs"

        let options =
            { FSharpParsingOptions.Default with
                SourceFiles = [| parsePath |]
                IsInteractive = source.Kind = Script
                IsExe = source.Kind = Implementation }

        parsePath, options

    let tokenizer (source: SourceFile) =
        let _, options = parsingArguments source
        let defines = CompilerEnvironment.GetConditionalDefinesForEditing options
        FSharpSourceTokenizer(defines, Some source.FullPath, None, None)
