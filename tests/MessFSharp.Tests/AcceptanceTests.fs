namespace MessFSharp.Tests

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open Xunit
open MessFSharp

module private PackagedTool =
    type Result =
        { ExitCode: int
          StandardOutput: string
          StandardError: string }

    let private repositoryRoot =
        let rec findRoot (directory: DirectoryInfo) =
            if File.Exists(Path.Combine(directory.FullName, "messfsharp.sln")) then
                directory.FullName
            elif isNull directory.Parent then
                failwith "Could not locate the repository root."
            else
                findRoot directory.Parent

        findRoot (DirectoryInfo(AppContext.BaseDirectory))

    let private configuration =
        if AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") then
            "Release"
        else
            "Debug"

    let private execute workingDirectory executable arguments =
        let startInfo = ProcessStartInfo(executable)
        startInfo.WorkingDirectory <- workingDirectory
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true

        for argument in arguments do
            startInfo.ArgumentList.Add(argument)

        use child = Process.Start(startInfo)
        let standardOutput = child.StandardOutput.ReadToEnd()
        let standardError = child.StandardError.ReadToEnd()
        child.WaitForExit()

        { ExitCode = child.ExitCode
          StandardOutput = standardOutput
          StandardError = standardError }

    let private executable =
        lazy
            let root =
                Path.Combine(Path.GetTempPath(), $"messfsharp-acceptance-{Environment.ProcessId}")

            let packages = Path.Combine(root, "packages")
            let tool = Path.Combine(root, "tool")
            Directory.CreateDirectory(packages) |> ignore
            Directory.CreateDirectory(tool) |> ignore

            let pack =
                execute
                    repositoryRoot
                    "dotnet"
                    [ "pack"
                      "src/MessFSharp/MessFSharp.fsproj"
                      "--configuration"
                      configuration
                      "--no-build"
                      "--no-restore"
                      "--output"
                      packages
                      "-p:PackageVersion=0.1.0-acceptance" ]

            if pack.ExitCode <> 0 then
                failwith $"Could not pack acceptance-test tool.\n{pack.StandardOutput}\n{pack.StandardError}"

            let install =
                execute
                    repositoryRoot
                    "dotnet"
                    [ "tool"
                      "install"
                      "--tool-path"
                      tool
                      "--add-source"
                      packages
                      "--version"
                      "0.1.0-acceptance"
                      "messfsharp" ]

            if install.ExitCode <> 0 then
                failwith $"Could not install acceptance-test tool.\n{install.StandardOutput}\n{install.StandardError}"

            Path.Combine(
                tool,
                if OperatingSystem.IsWindows() then
                    "messfsharp.exe"
                else
                    "messfsharp"
            )

    let run arguments =
        execute repositoryRoot executable.Value arguments

    let path relativePath =
        Path.Combine(repositoryRoot, relativePath)

module AcceptanceTests =
    let private newline = Environment.NewLine

    let private fixture name =
        PackagedTool.path (Path.Combine("tests", "Fixtures", name))

    [<Fact>]
    let ``packaged executable has exact help and version behavior`` () =
        let help = PackagedTool.run [ "--help" ]
        Assert.Equal(0, help.ExitCode)
        Assert.Equal(Cli.usage + newline, help.StandardOutput)
        Assert.Equal("", help.StandardError)

        let version = PackagedTool.run [ "--version" ]
        Assert.Equal(0, version.ExitCode)
        Assert.Equal("messfsharp 0.1.0" + newline, version.StandardOutput)
        Assert.Equal("", version.StandardError)

    [<Fact>]
    let ``packaged executable with no arguments reports exact usage failure`` () =
        let result = PackagedTool.run []
        Assert.Equal(1, result.ExitCode)
        Assert.Equal("", result.StandardOutput)

        Assert.Equal(
            "error: No arguments supplied."
            + newline
            + Cli.usage
            + newline
            + "Try 'messfsharp --help' for usage."
            + newline,
            result.StandardError
        )

    [<Fact>]
    let ``short v is verbose and not the version alias`` () =
        let result = PackagedTool.run [ "-v" ]

        Assert.Equal(1, result.ExitCode)
        Assert.Equal("", result.StandardOutput)

        Assert.Equal(
            "error: Analysis requires exactly three positional arguments: <paths> <format> <ruleset[,ruleset...]>."
            + newline
            + "Try 'messfsharp --help' for usage."
            + newline,
            result.StandardError
        )

    [<Fact>]
    let ``malformed command-line shapes fail with exact diagnostics`` () =
        let source = fixture "clean.fs"

        let cases =
            [ [ source; "text" ],
              "Analysis requires exactly three positional arguments: <paths> <format> <ruleset[,ruleset...]>.<END>"
              [ source; "unknown"; "fsharp" ], "Unknown report format 'unknown'.<END>"
              [ source; "text"; "fsharp"; "--unknown" ], "Unknown option '--unknown'.<END>"
              [ source; "text"; "fsharp"; "--reportfile" ], "--reportfile requires a value.<END>"
              [ source; "text"; "fsharp"; "--minimumpriority"; "high" ],
              "--minimumpriority expects a priority between 1 and 5, received 'high'.<END>" ]

        for arguments, expectedTemplate in cases do
            let result = PackagedTool.run arguments
            Assert.Equal(1, result.ExitCode)
            Assert.Equal("", result.StandardOutput)

            let expected =
                "error: "
                + expectedTemplate.Replace("<END>", newline + "Try 'messfsharp --help' for usage." + newline)

            Assert.Equal(expected, result.StandardError)

    [<Fact>]
    let ``unknown rulesets and missing paths are complete operational reports`` () =
        let cases =
            [ [ fixture "clean.fs"; "json"; "not-a-ruleset" ], "Unknown ruleset 'not-a-ruleset'."
              [ PackagedTool.path "tests/Fixtures/not-present.fs"; "json"; "fsharp" ], "Requested path does not exist." ]

        for arguments, expectedMessage in cases do
            let result = PackagedTool.run arguments
            Assert.Equal(1, result.ExitCode)
            Assert.Contains(expectedMessage, result.StandardError)

            use document = JsonDocument.Parse(result.StandardOutput)
            Assert.Equal(1, document.RootElement.GetProperty("errors").GetArrayLength())

            Assert.Equal(
                expectedMessage,
                (document.RootElement.GetProperty("errors")[0]).GetProperty("message").GetString()
            )

    [<Fact>]
    let ``discovery options and duplicate paths are honored by the packaged tool`` () =
        let directory = Directory.CreateTempSubdirectory("messfsharp-discovery-")

        try
            let keep = Path.Combine(directory.FullName, "Keep.fs")
            File.WriteAllText(keep, "module Keep\nlet answer = 42\n")

            let testDirectory =
                Directory.CreateDirectory(Path.Combine(directory.FullName, "Sample.Tests"))

            File.WriteAllText(
                Path.Combine(testDirectory.FullName, "Bad.fs"),
                "module Bad\nlet mutable shared = 0\nshared <- 1\n"
            )

            let excludedDirectory =
                Directory.CreateDirectory(Path.Combine(directory.FullName, "excluded"))

            File.WriteAllText(Path.Combine(excludedDirectory.FullName, "Bad.fs"), "this is not valid F#")

            let custom = Path.Combine(directory.FullName, "Only.custom")
            File.WriteAllText(custom, "module Custom\nlet answer = 42\n")

            let filtered =
                PackagedTool.run
                    [ directory.FullName
                      "json"
                      "fsharp"
                      "--ignore-tests"
                      "--exclude"
                      "excluded" ]

            Assert.Equal(0, filtered.ExitCode)
            Assert.Equal("", filtered.StandardError)

            use filteredReport = JsonDocument.Parse(filtered.StandardOutput)
            Assert.Equal(0, filteredReport.RootElement.GetProperty("errors").GetArrayLength())
            Assert.Equal(0, filteredReport.RootElement.GetProperty("violations").GetArrayLength())

            let suffixOverride =
                PackagedTool.run [ directory.FullName; "json"; "fsharp"; "--suffixes"; "custom" ]

            Assert.Equal(0, suffixOverride.ExitCode)
            Assert.Equal("", suffixOverride.StandardError)

            use suffixReport = JsonDocument.Parse(suffixOverride.StandardOutput)
            Assert.Equal(0, suffixReport.RootElement.GetProperty("errors").GetArrayLength())
            Assert.Equal(0, suffixReport.RootElement.GetProperty("violations").GetArrayLength())

            let single = PackagedTool.run [ fixture "bad.fs"; "json"; "fsharp" ]
            let duplicatePath = String.concat "," [ fixture "bad.fs"; fixture "bad.fs" ]
            let duplicate = PackagedTool.run [ duplicatePath; "json"; "fsharp" ]
            Assert.Equal(single.ExitCode, duplicate.ExitCode)
            Assert.Equal(single.StandardOutput, duplicate.StandardOutput)
            Assert.Equal(single.StandardError, duplicate.StandardError)
        finally
            directory.Delete(true)

    [<Fact>]
    let ``mutually recursive and bindings are analyzed`` () =
        let result =
            PackagedTool.run [ fixture "recursive-functions.fs"; "json"; "controversial" ]

        Assert.Equal(2, result.ExitCode)
        Assert.Equal("", result.StandardError)

        use document = JsonDocument.Parse(result.StandardOutput)

        let findings =
            document.RootElement.GetProperty("violations").EnumerateArray()
            |> Seq.map (fun finding ->
                finding.GetProperty("rule").GetString(),
                finding.GetProperty("startLine").GetInt32(),
                finding.GetProperty("description").GetString())
            |> Seq.toList

        Assert.Contains(("CamelCaseVariableName", 5, "Variable name 'BadOdd' should use camelCase."), findings)

    [<Fact>]
    let ``processing errors take precedence and ignore flags only change exit codes`` () =
        let paths = String.concat "," [ fixture "invalid.fs"; fixture "bad.fs" ]

        let run extra =
            PackagedTool.run ([ paths; "json"; "fsharp" ] @ extra)

        let ordinary = run []
        let ignoreErrors = run [ "--ignore-errors-on-exit" ]
        let ignoreViolations = run [ "--ignore-violations-on-exit" ]
        let ignoreBoth = run [ "--ignore-errors-on-exit"; "--ignore-violations-on-exit" ]

        Assert.Equal(1, ordinary.ExitCode)
        Assert.Equal(2, ignoreErrors.ExitCode)
        Assert.Equal(1, ignoreViolations.ExitCode)
        Assert.Equal(0, ignoreBoth.ExitCode)

        for result in [ ordinary; ignoreErrors; ignoreViolations; ignoreBoth ] do
            use document = JsonDocument.Parse(result.StandardOutput)
            Assert.NotEqual(0, document.RootElement.GetProperty("errors").GetArrayLength())
            Assert.NotEqual(0, document.RootElement.GetProperty("violations").GetArrayLength())

    [<Fact>]
    let ``report file replaces content and leaves standard output empty`` () =
        let path =
            Path.Combine(Path.GetTempPath(), $"messfsharp-report-{Guid.NewGuid():N}.json")

        File.WriteAllText(path, "stale content")

        try
            let result =
                PackagedTool.run [ fixture "clean.fs"; "json"; "fsharp"; "--reportfile"; path ]

            Assert.Equal(0, result.ExitCode)
            Assert.Equal("", result.StandardOutput)
            Assert.Equal("", result.StandardError)

            use document = JsonDocument.Parse(File.ReadAllText(path))
            Assert.Equal("messfsharp", document.RootElement.GetProperty("tool").GetString())
            Assert.Equal(0, document.RootElement.GetProperty("errors").GetArrayLength())
            Assert.Equal(0, document.RootElement.GetProperty("violations").GetArrayLength())
        finally
            File.Delete(path)

    [<Fact>]
    let ``verbose short option surfaces referenced-rule warnings without changing the report`` () =
        let rulesetPath = fixture "unknown-reference.xml"

        let result = PackagedTool.run [ fixture "clean.fs"; "json"; rulesetPath; "-v" ]

        Assert.Equal(0, result.ExitCode)

        Assert.Equal(
            $"warning: Unknown referenced rule 'NoSuchRule' in '{Path.GetFullPath(rulesetPath)}'."
            + newline,
            result.StandardError
        )

        use document = JsonDocument.Parse(result.StandardOutput)
        Assert.Equal(0, document.RootElement.GetProperty("errors").GetArrayLength())
        Assert.Equal(0, document.RootElement.GetProperty("violations").GetArrayLength())

    [<Fact>]
    let ``every rule that can match valid F sharp has an executable positive fixture`` () =
        let result =
            PackagedTool.run
                [ PackagedTool.path "tests/Fixtures"
                  "json"
                  fixture "all-rules.xml"
                  "--exclude"
                  "invalid.fs" ]

        Assert.Equal(2, result.ExitCode)
        Assert.Equal("", result.StandardError)

        use document = JsonDocument.Parse(result.StandardOutput)

        let actual =
            document.RootElement.GetProperty("violations").EnumerateArray()
            |> Seq.map (fun finding -> finding.GetProperty("rule").GetString())
            |> Set.ofSeq

        let compatibilityOnly = set [ "IfStatementAssignment"; "GotoStatement" ]

        let expected =
            Rules.all
            |> List.map (fun rule -> rule.Name)
            |> Set.ofList
            |> fun rules -> Set.difference rules compatibilityOnly

        Assert.Equal<Set<string>>(expected, actual)
        Assert.DoesNotContain("IfStatementAssignment", actual)
        Assert.DoesNotContain("GotoStatement", actual)

    [<Fact>]
    let ``every bundled rule stays quiet across multiple idiomatic negative fixtures`` () =
        let paths =
            String.concat "," [ fixture "idiomatic-one.fs"; fixture "idiomatic-two.fs" ]

        let result =
            PackagedTool.run [ paths; "json"; "codesize,naming,unusedcode,cleancode,design,controversial" ]

        Assert.Equal(0, result.ExitCode)
        Assert.Equal("", result.StandardError)

        use document = JsonDocument.Parse(result.StandardOutput)
        Assert.Equal(0, document.RootElement.GetProperty("errors").GetArrayLength())
        Assert.Equal(0, document.RootElement.GetProperty("violations").GetArrayLength())
