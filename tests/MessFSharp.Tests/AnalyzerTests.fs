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

    let private analyzeSource text =
        let source =
            { FullPath = Path.Combine(Path.GetTempPath(), "messfsharp-model.fs")
              Kind = Implementation
              Text = text
              Lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n') }

        match Parsing.parse source with
        | Ok parsedInput -> Model.analyze source parsedInput
        | Error errors -> failwithf "Expected valid F# source, got %A" errors

    [<Fact>]
    let ``compiler syntax supplies data types interfaces scopes expressions and references`` () =
        let analyzed =
            analyzeSource
                """module ModelShapes

type Email = Email of string

type IClock =
    abstract member Now: unit -> System.DateTime

let choose condition left right =
    if condition then left else right
"""

        let typeNamed name =
            analyzed.Declarations
            |> List.find (fun declaration -> declaration.Kind = Type && declaration.Name = name)

        Assert.Equal(UnionType, (typeNamed "Email").TypeShape)
        Assert.True((typeNamed "Email").IsUnion)
        Assert.Equal(InterfaceType, (typeNamed "IClock").TypeShape)
        Assert.True((typeNamed "IClock").IsInterface)

        Assert.Contains(
            analyzed.Declarations,
            fun declaration -> declaration.Kind = UnionCase && declaration.Name = "Email"
        )

        Assert.Contains(analyzed.Expressions, fun expression -> expression.Kind = ConditionalExpression)
        Assert.Contains(analyzed.SyntacticReferences, fun reference -> reference.Name = "condition")
        Assert.NotEmpty(analyzed.LexicalScopes)

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
    let ``case-sensitive file names are not collapsed on Unix`` () =
        if OperatingSystem.IsLinux() then
            let directory = Directory.CreateTempSubdirectory("messfsharp-case-")

            try
                let upper = Path.Combine(directory.FullName, "A.fs")
                let lower = Path.Combine(directory.FullName, "a.fs")
                File.WriteAllText(upper, "module A")
                File.WriteAllText(lower, "module B")

                let discovered, errors =
                    Discovery.discover (options [ directory.FullName ] [ "fsharp" ] Text)

                Assert.Empty(errors)
                Assert.Equal<string list>([ upper; lower ], discovered)
            finally
                directory.Delete(true)

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

    [<Fact>]
    let ``double-backtick identifiers with spaces are scanned and reference counted`` () =
        let analyzed =
            analyzeSource
                """module TestUnused
let calculate () =
    let ``my count`` = 1
    let result = ``my count`` + 1
    result
"""

        let selection =
            { Name = "UnusedLocalVariable"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "UnusedLocalVariable")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``verbatim string with trailing backslash does not swallow subsequent tokens`` () =
        let analyzed =
            analyzeSource
                """module TestVerbatim
let dir = @"C:\temp\"
let mutable count = 0
count <- 1
"""

        let selection =
            { Name = "GlobalVariable"
              RulesetName = "design"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "GlobalVariable")
        let violations = rule.Check analyzed selection
        Assert.NotEmpty(violations)

    [<Fact>]
    let ``interpolated string holes are scanned and referenced bindings are not unused`` () =
        let analyzed =
            analyzeSource
                """module TestInterpolated
let greet (name: string) =
    let prefix = "Hello"
    printfn $"{prefix} {name}"
"""

        let selectionParam =
            { Name = "UnusedFormalParameter"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let ruleParam = Rules.all |> List.find (fun r -> r.Name = "UnusedFormalParameter")
        let violationsParam = ruleParam.Check analyzed selectionParam
        Assert.Empty(violationsParam)

        let selectionLocal =
            { Name = "UnusedLocalVariable"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let ruleLocal = Rules.all |> List.find (fun r -> r.Name = "UnusedLocalVariable")
        let violationsLocal = ruleLocal.Check analyzed selectionLocal
        Assert.Empty(violationsLocal)

    [<Fact>]
    let ``duplicated array key ignores commas in map entry values`` () =
        let analyzed =
            analyzeSource
                """module TestMap
let myMap =
    Map.ofList
        [ "first", (1, "shared")
          "second", (1, "different") ]
"""

        let selection =
            { Name = "DuplicatedArrayKey"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "DuplicatedArrayKey")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``static access ignores open directives`` () =
        let analyzed =
            analyzeSource
                """module TestStatic
open System.Collections.Generic
open System.Text.Json

let run () = 42
"""

        let selection =
            { Name = "StaticAccess"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "StaticAccess")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``boolean argument flag does not match unanchored substring use in words like isUser or paused`` () =
        let analyzed =
            analyzeSource
                """module TestFlags
type Account =
    member this.Update(isUser: bool, isPaused: bool) = ()
"""

        let selection =
            { Name = "BooleanArgumentFlag"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "BooleanArgumentFlag")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``else expression does not flag outer else when only nested branch terminates`` () =
        let analyzed =
            analyzeSource
                """module TestElse
let check (a: bool) (b: bool) =
    if a then
        if b then
            failwith "nested termination"
        printfn "continuing outer then branch"
    else
        printfn "outer else branch"
"""

        let selection =
            { Name = "ElseExpression"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "ElseExpression")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``suppress message on let binding with preceding attribute suppresses rule violation`` () =
        let source =
            """module TestSuppression
open System.Diagnostics.CodeAnalysis

[<SuppressMessage("messfsharp", "ShortVariable")>]
let v = 1
"""

        let tempFile =
            Path.Combine(Path.GetTempPath(), $"messfsharp-suppress-{Guid.NewGuid()}.fs")

        try
            File.WriteAllText(tempFile, source)
            let result = Engine.run "0.1.0" (options [ tempFile ] [ "naming" ] Json)
            Assert.Empty(result.Report.Errors)
            Assert.Empty(result.Report.Violations)
        finally
            File.Delete(tempFile)

    [<Fact>]
    let ``attributed interface declarations resolve InterfaceType and IsInterface true`` () =
        let source =
            """module TestInterface
open System

[<Interface>]
type IGreeter =
    abstract member Greet: string -> string
"""

        let analyzed = analyzeSource source

        let typeDecl =
            analyzed.Declarations
            |> List.find (fun d -> d.Kind = Type && d.Name = "IGreeter")

        Assert.Equal(InterfaceType, typeDecl.TypeShape)
        Assert.True(typeDecl.IsInterface)
        Assert.False(typeDecl.IsClassLike)
