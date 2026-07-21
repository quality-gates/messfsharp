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
    let ``invalid command shape is reported as an error`` () =
        match Cli.parse [| "src"; "text" |] with
        | Invalid message -> Assert.Contains("exactly three positional", message)
        | other -> Assert.True(false, sprintf "Expected Invalid, got %A" other)
