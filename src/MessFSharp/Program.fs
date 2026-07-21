namespace MessFSharp

open System
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
module Program =
    let version =
        let assembly = Reflection.Assembly.GetExecutingAssembly()

        let informational =
            assembly.GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)

        match informational with
        | [| attribute |] -> (attribute :?> Reflection.AssemblyInformationalVersionAttribute).InformationalVersion
        | _ -> "0.1.0"

    let exitCode =
        function
        | Help
        | Version -> 0
        | Invalid _ -> 1
        | Analyze _ -> 0

    [<EntryPoint>]
    let main argv =
        match Cli.parse argv with
        | Help ->
            Console.Out.WriteLine(Cli.usage)
            0
        | Version ->
            Console.Out.WriteLine(sprintf "messfsharp %s" version)
            0
        | Invalid message ->
            Console.Error.WriteLine(sprintf "error: %s" message)

            if argv.Length = 0 then
                Console.Error.WriteLine(Cli.usage)

            Console.Error.WriteLine("Try 'messfsharp --help' for usage.")
            1
        | Analyze options ->
            let result = Engine.run version options

            if options.Verbose then
                for warning in result.Warnings do
                    Console.Error.WriteLine(sprintf "warning: %s" warning)

            for error in result.Report.Errors do
                let location =
                    error.Location
                    |> Option.map (fun item -> sprintf ":%d:%d" item.StartLine item.StartColumn)
                    |> Option.defaultValue ""

                Console.Error.WriteLine(
                    sprintf "%s%s: error: %s" (error.File |> Option.defaultValue "messfsharp") location error.Message
                )

            match Engine.writeReport options result.Report with
            | Ok() -> result.ExitCode
            | Error message ->
                Console.Error.WriteLine(sprintf "error: %s" message)
                1
