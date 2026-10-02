namespace MessFSharp.Tests

open Xunit
open MessFSharp
open MessFSharp.Domain

module CliTests =
    [<Fact>]
    let ``help aliases select help without positional arguments`` () =
        Assert.Equal(Help, Cli.parse [| "--help" |])
        Assert.Equal(Help, Cli.parse [| "help" |])
        Assert.Equal(Help, Cli.parse [| "-h"; "anything" |])

    [<Fact>]
    let ``analysis parses the public command shape and comma separated values`` () =
        match
            Cli.parse
                [| "src,tests"
                   "json"
                   "fsharp,codesize"
                   "--ignore-tests"
                   "--minimumpriority"
                   "2" |]
        with
        | Analyze options ->
            Assert.True(options.Paths = [ "src"; "tests" ])
            Assert.Equal(Json, options.Format)
            Assert.True(options.Rulesets = [ "fsharp"; "codesize" ])
            Assert.Equal(Some 2, options.MinimumPriority)
            Assert.True(options.IgnoreTests)
        | other -> Assert.True(false, sprintf "Expected Analyze, got %A" other)

    [<Fact>]
    let ``enable and only are aliases that collect one requested rule list`` () =
        let parsed =
            [ Cli.parse
                  [| "src"
                     "text"
                     "fsharp"
                     "--enable"
                     "ShortVariable,LongVariable"
                     "--only"
                     "ElseExpression" |]
              Cli.parse
                  [| "src"
                     "text"
                     "fsharp"
                     "--only"
                     "ShortVariable,LongVariable"
                     "--enable"
                     "ElseExpression" |]
              Cli.parse
                  [| "src"
                     "text"
                     "fsharp"
                     "--only"
                     "ShortVariable,LongVariable"
                     "--only"
                     "ElseExpression" |] ]

        let expected =
            Analyze
                { Defaults.analysisOptions with
                    Paths = [ "src" ]
                    Rulesets = [ "fsharp" ]
                    Only = [ "ShortVariable"; "LongVariable"; "ElseExpression" ] }

        for command in parsed do
            Assert.Equal(expected, command)

    [<Fact>]
    let ``analysis parses the report path base directory option`` () =
        match Cli.parse [| "src"; "json"; "fsharp"; "--basedir"; "repository" |] with
        | Analyze options -> Assert.Equal(Some "repository", options.BaseDirectory)
        | other -> Assert.True(false, sprintf "Expected Analyze, got %A" other)

    [<Fact>]
    let ``help documents the report path base directory option`` () =
        Assert.Contains("--basedir <path>", Cli.usage)

    [<Fact>]
    let ``invalid command shape is reported as an error`` () =
        match Cli.parse [| "src"; "text" |] with
        | Invalid message -> Assert.Contains("exactly three positional", message)
        | other -> Assert.True(false, sprintf "Expected Invalid, got %A" other)

    [<Fact>]
    let ``argument values matching help do not hijack command parsing`` () =
        match Cli.parse [| "src"; "text"; "fsharp"; "--exclude"; "help" |] with
        | Analyze options -> Assert.Equal<string list>([ "help" ], options.Excludes)
        | other -> Assert.True(false, sprintf "Expected Analyze, got %A" other)

        match Cli.parse [| "help"; "text"; "fsharp" |] with
        | Analyze options -> Assert.Equal<string list>([ "help" ], options.Paths)
        | other -> Assert.True(false, sprintf "Expected Analyze, got %A" other)
