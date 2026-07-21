namespace MessFSharp.Tests

open System
open System.IO
open Xunit
open MessFSharp
open MessFSharp.Domain

module AnalyzerTests =
    let private fixture name =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", name))

    let private options paths rulesets format =
        { Defaults.analysisOptions with
            Paths = paths
            Rulesets = rulesets
            Format = format }

    [<Fact>]
    let ``recommended ruleset keeps idiomatic F sharp fixture clean`` () =
        let result = Engine.run "0.1.0" (options [ fixture "clean.fs" ] [ "fsharp" ] Text)
        Assert.Empty(result.Report.Errors)
        Assert.Empty(result.Report.Violations)
        Assert.Equal(0, result.ExitCode)

    [<Fact>]
    let ``analyzer reports violations with automation exit code`` () =
        let result = Engine.run "0.1.0" (options [ fixture "bad.fs" ] [ "fsharp" ] Json)
        Assert.Empty(result.Report.Errors)
        Assert.Contains(result.Report.Violations, fun violation -> violation.RuleName = "GlobalVariable")
        Assert.Equal(2, result.ExitCode)

    [<Fact>]
    let ``component rulesets retain stricter checks omitted by fsharp`` () =
        let unwrap loaded =
            match loaded with
            | Ok value -> value
            | Error errors -> failwith (String.concat "; " errors)

        let fsharp: Rulesets.Loaded = Rulesets.load [ "fsharp" ] |> unwrap
        let opinionated: Rulesets.Loaded = Rulesets.load [ "opinionated" ] |> unwrap

        let fsharpNames =
            fsharp.Selections |> List.map (fun (selection: RuleSelection) -> selection.Name)

        let opinionatedNames =
            opinionated.Selections
            |> List.map (fun (selection: RuleSelection) -> selection.Name)

        Assert.DoesNotContain("ShortVariable", fsharpNames)
        Assert.DoesNotContain("UnusedFormalParameter", fsharpNames)
        Assert.Contains("ShortVariable", opinionatedNames)
        Assert.Contains("UnusedFormalParameter", opinionatedNames)

    [<Fact>]
    let ``multiple input paths are deterministic and duplicate free`` () =
        let clean = fixture "clean.fs"

        let discovered, errors =
            Discovery.discover (options [ clean; clean ] [ "fsharp" ] Text)

        Assert.Empty(errors)
        Assert.Equal<string list>([ clean ], discovered)

    [<Fact>]
    let ``a parse failure is reported without suppressing valid input processing`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "invalid.fs"; fixture "bad.fs" ] [ "fsharp" ] Json)

        Assert.NotEmpty(result.Report.Errors)

        Assert.Contains(
            result.Report.Violations,
            fun violation -> violation.Location.File.EndsWith("bad.fs", StringComparison.Ordinal)
        )

        Assert.Equal(1, result.ExitCode)

    [<Fact>]
    let ``custom rulesets apply overrides and deduplicate rule identities`` () =
        let loaded =
            match Rulesets.load [ fixture "custom-ruleset.xml" ] with
            | Ok value -> value
            | Error errors -> failwith (String.concat "; " errors)

        let longVariable =
            loaded.Selections
            |> List.find (fun selection -> selection.Name = "LongVariable")

        Assert.Equal(1, longVariable.Priority)
        Assert.Equal(Some "35", Map.tryFind "maximum" longVariable.Properties)

        Assert.Equal(
            1,
            loaded.Selections
            |> List.filter (fun selection -> selection.Name = "LongVariable")
            |> List.length
        )

    [<Fact>]
    let ``direct unknown custom rules are operational errors`` () =
        match Rulesets.load [ fixture "unknown-rule.xml" ] with
        | Error errors -> Assert.Contains(errors, fun error -> error.Contains("NoSuchRule", StringComparison.Ordinal))
        | Ok _ -> Assert.True(false, "Expected an unknown direct rule to fail ruleset loading.")

    [<Fact>]
    let ``unknown referenced rules are warnings without substituted rules`` () =
        match Rulesets.load [ fixture "unknown-reference.xml" ] with
        | Ok loaded ->
            Assert.Empty(loaded.Selections)
            Assert.Contains(loaded.Warnings, fun warning -> warning.Contains("NoSuchRule", StringComparison.Ordinal))
        | Error errors -> Assert.True(false, sprintf "Expected a warning-only ruleset, got %A" errors)

    [<Fact>]
    let ``signature files are parsed as public declarations`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "signature.fsi" ] [ "fsharp" ] Json)

        Assert.Empty(result.Report.Errors)
        Assert.Empty(result.Report.Violations)

    [<Fact>]
    let ``unwritten mutable module values remain quiet`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "quiet-mutable.fs" ] [ "design" ] Json)

        Assert.Empty(result.Report.Errors)
        Assert.DoesNotContain(result.Report.Violations, fun violation -> violation.RuleName = "GlobalVariable")

    [<Fact>]
    let ``npath measures alternatives rather than exponentiating cyclomatic complexity`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "branching.fs" ] [ fixture "npath-ruleset.xml" ] Json)

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "NPathComplexity"
                && violation.Description.Contains("NPath complexity 2", StringComparison.Ordinal)
        )

    [<Fact>]
    let ``npath counts nested alternatives`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "nested-branching.fs" ] [ fixture "npath-ruleset.xml" ] Json)

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "NPathComplexity"
                && violation.Description.Contains("NPath complexity 3", StringComparison.Ordinal)
        )

    [<Fact>]
    let ``npath multiplies independent match alternatives`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "multiple-matches.fs" ] [ fixture "npath-ruleset.xml" ] Json)

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "NPathComplexity"
                && violation.Description.Contains("NPath complexity 6", StringComparison.Ordinal)
        )

    [<Fact>]
    let ``variable casing checks functions as well as values`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "bad.fs" ] [ "controversial" ] Json)

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "CamelCaseVariableName"
                && violation.Description.Contains("BadFunction", StringComparison.Ordinal)
        )

    [<Fact>]
    let ``multiline rules and variable property overrides retain actionable findings`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "edge-cases.fs" ] [ fixture "edge-ruleset.xml" ] Json)

        Assert.Empty(result.Report.Errors)

        let hasRuleAtLine rule line =
            result.Report.Violations
            |> List.exists (fun violation -> violation.RuleName = rule && violation.Location.StartLine = line)

        Assert.True(hasRuleAtLine "BooleanArgumentFlag" 5)
        Assert.True(hasRuleAtLine "ElseExpression" 26)
        Assert.True(hasRuleAtLine "ExcessiveParameterList" 5)
        Assert.True(hasRuleAtLine "CountInLoopExpression" 9)
        Assert.True(hasRuleAtLine "DuplicatedArrayKey" 13)
        Assert.True(hasRuleAtLine "EmptyCatchBlock" 20)

        Assert.Equal(
            2,
            result.Report.Violations
            |> List.filter (fun violation -> violation.RuleName = "DuplicatedArrayKey")
            |> List.length
        )

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "LongVariable"
                && violation.Description.Contains("ordinaryLongNameThatShouldBeReported", StringComparison.Ordinal)
        )

        Assert.DoesNotContain(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "LongVariable"
                && (violation.Description.Contains("prefixLongName", StringComparison.Ordinal)
                    || violation.Description.Contains("veryLongSuffix", StringComparison.Ordinal)
                    || violation.Description.Contains("exemptLongName", StringComparison.Ordinal))
        )

        Assert.DoesNotContain(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "ShortVariable"
                && violation.Description.Contains("'n'", StringComparison.Ordinal)
        )

        Assert.Single(
            result.Report.Violations
            |> List.filter (fun violation -> violation.RuleName = "StaticAccess")
        )

    [<Fact>]
    let ``nested custom ruleset exclusions are inherited`` () =
        let loaded =
            match Rulesets.load [ fixture "nested-ruleset.xml" ] with
            | Ok value -> value
            | Error errors -> failwith (String.concat "; " errors)

        let names = loaded.Selections |> List.map (fun selection -> selection.Name)
        Assert.DoesNotContain("ShortClassName", names)
        Assert.DoesNotContain("LongClassName", names)
        Assert.Contains("LongVariable", names)

    [<Fact>]
    let ``access modified types and multiline parameter roles are modeled`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "edge-cases.fs" ] [ "controversial" ] Json)

        Assert.Empty(result.Report.Errors)

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "CamelCaseClassName"
                && violation.Description.Contains("privateThing", StringComparison.Ordinal)
        )

    [<Fact>]
    let ``strict mode restores declaration suppressions`` () =
        let normal =
            Engine.run "0.1.0" (options [ fixture "suppressed.fs" ] [ "design" ] Json)

        let strict =
            Engine.run
                "0.1.0"
                { options [ fixture "suppressed.fs" ] [ "design" ] Json with
                    Strict = true }

        Assert.Empty(normal.Report.Errors)
        Assert.Empty(normal.Report.Violations)
        Assert.Contains(strict.Report.Violations, fun violation -> violation.RuleName = "GlobalVariable")

    [<Fact>]
    let ``pattern and generic parameter syntax stays out of naming findings`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "pattern-parameters.fs" ] [ "controversial" ] Json)

        Assert.Empty(result.Report.Errors)
        Assert.Empty(result.Report.Violations)

    [<Fact>]
    let ``unused formal parameters respect shadowed lexical bindings`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "shadowed-bindings.fs" ] [ "unusedcode" ] Json)

        Assert.Empty(result.Report.Errors)

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "UnusedFormalParameter"
                && violation.Location.StartLine = 3
                && violation.Description.Contains("'value'", StringComparison.Ordinal)
        )

        Assert.DoesNotContain(
            result.Report.Violations,
            fun violation -> violation.RuleName = "UnusedFormalParameter" && violation.Location.StartLine = 4
        )

    [<Fact>]
    let ``primary constructors contribute their parameter groups`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "constructor.fs" ] [ fixture "constructor-ruleset.xml" ] Json)

        Assert.Empty(result.Report.Errors)

        Assert.Contains(
            result.Report.Violations,
            fun violation ->
                violation.RuleName = "ExcessiveParameterList"
                && violation.Description.Contains("Parameter count 2", StringComparison.Ordinal)
        )

    [<Fact>]
    let ``exit expressions are token based rather than text based`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "exit-cases.fs" ] [ fixture "exit-ruleset.xml" ] Json)

        Assert.Empty(result.Report.Errors)

        Assert.Equal(
            2,
            result.Report.Violations
            |> List.filter (fun violation -> violation.RuleName = "ExitExpression")
            |> List.length
        )
