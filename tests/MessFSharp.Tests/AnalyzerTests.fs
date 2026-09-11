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
    let ``compiler bindings match existing member and property declarations`` () =
        let analyzed =
            analyzeSource
                """module TestMemberBindings

type Service() =
    member private this.Helper() = 42
    member this.Compute(amount: int) = this.Helper() + amount
    member this.Rate with get() = 1
"""

        let declarationCount name =
            analyzed.Declarations
            |> List.filter (fun declaration -> declaration.Name = name)
            |> List.length

        Assert.Equal(1, declarationCount "Helper")
        Assert.Equal(1, declarationCount "Compute")
        Assert.Equal(1, declarationCount "Rate")

    [<Fact>]
    let ``operator compiler bindings retain one source-facing declaration`` () =
        let analyzed =
            analyzeSource
                """module Sample

let ( +++ ) left right = left + right
"""

        let operatorNames =
            analyzed.Declarations
            |> List.filter (fun declaration ->
                declaration.Parent = Some "Sample"
                && declaration.IsPublic
                && (declaration.Kind = Function || declaration.Kind = Value))
            |> List.map (fun declaration -> declaration.Name)

        Assert.Equal<string list>([ "+++" ], operatorNames)

    [<Fact>]
    let ``tuple destructuring bindings are values rather than functions and parameters`` () =
        let analyzed =
            analyzeSource
                """module Sample

let (first, second) = (1, 2)
let total = first + second
"""

        let declaration name =
            analyzed.Declarations |> List.find (fun item -> item.Name = name)

        let first = declaration "first"
        let second = declaration "second"

        Assert.Equal(Value, first.Kind)
        Assert.False(first.IsFunction)
        Assert.Equal(0, first.ParameterCount)
        Assert.Equal(Some "Sample", first.Parent)
        Assert.Equal(Value, second.Kind)
        Assert.False(second.IsFunction)
        Assert.Equal(0, second.ParameterCount)
        Assert.Equal(Some "Sample", second.Parent)
        Assert.DoesNotContain(analyzed.Declarations, fun item -> item.Kind = Parameter)

        Assert.DoesNotContain(
            analyzed.TypeMethods |> Map.tryFind "Sample" |> Option.defaultValue [],
            fun item -> item.Name = "first" || item.Name = "second"
        )

        let rule = Rules.all |> List.find (fun item -> item.Name = "UnusedFormalParameter")

        let selection =
            { Name = "UnusedFormalParameter"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        Assert.Empty(rule.Check analyzed selection)

    [<Fact>]
    let ``record pattern bindings are values rather than field labels and parameters`` () =
        let analyzed =
            analyzeSource
                """module Sample

type Pair = { First: int; Second: int }

let readFirst () =
    let { First = usedFirst; Second = unusedSecond } = { First = 1; Second = 2 }
    usedFirst
"""

        let declaration name =
            analyzed.Declarations |> List.find (fun item -> item.Name = name)

        let usedFirst = declaration "usedFirst"
        let unusedSecond = declaration "unusedSecond"

        Assert.Equal(Value, usedFirst.Kind)
        Assert.False(usedFirst.IsFunction)
        Assert.Equal(0, usedFirst.ParameterCount)
        Assert.Equal(Some "readFirst", usedFirst.Parent)
        Assert.False(usedFirst.IsModuleLevel)
        Assert.Equal(Value, unusedSecond.Kind)
        Assert.False(unusedSecond.IsFunction)
        Assert.Equal(0, unusedSecond.ParameterCount)
        Assert.Equal(Some "readFirst", unusedSecond.Parent)

        Assert.DoesNotContain(
            analyzed.Declarations,
            fun item -> (item.Kind = Value || item.Kind = Parameter) && item.Name = "First"
        )

        let unusedLocalRule =
            Rules.all |> List.find (fun item -> item.Name = "UnusedLocalVariable")

        let localSelection =
            { Name = "UnusedLocalVariable"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let unusedLocalViolations = unusedLocalRule.Check analyzed localSelection

        Assert.Equal<string list>(
            [ "Local binding 'unusedSecond' is never used." ],
            unusedLocalViolations |> List.map (fun violation -> violation.Description)
        )

        let formalParameterRule =
            Rules.all |> List.find (fun item -> item.Name = "UnusedFormalParameter")

        let parameterSelection =
            { Name = "UnusedFormalParameter"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        Assert.Empty(formalParameterRule.Check analyzed parameterSelection)

        let camelCaseRule =
            Rules.all |> List.find (fun item -> item.Name = "CamelCaseVariableName")

        let camelSelection =
            { Name = "CamelCaseVariableName"
              RulesetName = "controversial"
              Priority = 3
              Properties = Map.empty }

        Assert.DoesNotContain(
            camelCaseRule.Check analyzed camelSelection,
            fun violation -> violation.Description.Contains("'First'")
        )

    [<Fact>]
    let ``list pattern bindings are values rather than functions and parameters`` () =
        let analyzed =
            analyzeSource
                """module Sample

let readHead () =
    let [ usedHead; unusedTail ] = [ 1; 2 ]
    usedHead
"""

        let declaration name =
            analyzed.Declarations |> List.find (fun item -> item.Name = name)

        let usedHead = declaration "usedHead"
        let unusedTail = declaration "unusedTail"

        Assert.Equal(Value, usedHead.Kind)
        Assert.False(usedHead.IsFunction)
        Assert.Equal(0, usedHead.ParameterCount)
        Assert.Equal(Some "readHead", usedHead.Parent)
        Assert.False(usedHead.IsModuleLevel)
        Assert.Equal(Value, unusedTail.Kind)
        Assert.False(unusedTail.IsFunction)
        Assert.Equal(0, unusedTail.ParameterCount)
        Assert.Equal(Some "readHead", unusedTail.Parent)

        Assert.DoesNotContain(analyzed.Declarations, fun item -> item.Kind = Parameter)

        let unusedLocalRule =
            Rules.all |> List.find (fun item -> item.Name = "UnusedLocalVariable")

        let localSelection =
            { Name = "UnusedLocalVariable"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let unusedLocalViolations = unusedLocalRule.Check analyzed localSelection

        Assert.Equal<string list>(
            [ "Local binding 'unusedTail' is never used." ],
            unusedLocalViolations |> List.map (fun violation -> violation.Description)
        )

        let formalParameterRule =
            Rules.all |> List.find (fun item -> item.Name = "UnusedFormalParameter")

        let parameterSelection =
            { Name = "UnusedFormalParameter"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        Assert.Empty(formalParameterRule.Check analyzed parameterSelection)

    [<Fact>]
    let ``active pattern inputs are not reported as unused formal parameters`` () =
        let analyzed =
            analyzeSource
                """module Sample

let (|Even|Odd|) value =
    if value % 2 = 0 then Even else Odd
"""

        let valueParameter =
            analyzed.Declarations
            |> List.find (fun item -> item.Kind = Parameter && item.Name = "value")

        Assert.Equal(Some "|Even|Odd|", valueParameter.Parent)

        let rule = Rules.all |> List.find (fun item -> item.Name = "UnusedFormalParameter")

        let selection =
            { Name = "UnusedFormalParameter"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        Assert.Empty(rule.Check analyzed selection)

    [<Fact>]
    let ``let bang bindings are local to computation expressions`` () =
        let analyzed =
            analyzeSource
                """module Sample

let run work = async {
    let! result = work
    return 1
}
"""

        let resultDeclaration =
            analyzed.Declarations
            |> List.find (fun declaration -> declaration.Name = "result")

        let selection =
            { Name = "UnusedLocalVariable"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let rule =
            Rules.all |> List.find (fun candidate -> candidate.Name = "UnusedLocalVariable")

        let violations = rule.Check analyzed selection

        Assert.Contains(violations, fun violation -> violation.Description = "Local binding 'result' is never used.")
        Assert.False(resultDeclaration.IsModuleLevel)

    [<Fact>]
    let ``let bang bindings are local inside module-level computation values`` () =
        let analyzed =
            analyzeSource
                """module Sample

let workflow = async {
    let! result = work
    return ()
}
"""

        let resultDeclaration =
            analyzed.Declarations
            |> List.find (fun declaration -> declaration.Name = "result")

        let selection =
            { Name = "UnusedLocalVariable"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let rule =
            Rules.all |> List.find (fun candidate -> candidate.Name = "UnusedLocalVariable")

        let violations = rule.Check analyzed selection

        Assert.Contains(violations, fun violation -> violation.Description = "Local binding 'result' is never used.")
        Assert.False(resultDeclaration.IsModuleLevel)

    [<Fact>]
    let ``used let bang bindings inside module-level computation values are not unused`` () =
        let analyzed =
            analyzeSource
                """module Sample

let workflow = async {
    let! result = work
    return result
}
"""

        let selection =
            { Name = "UnusedLocalVariable"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let rule =
            Rules.all |> List.find (fun candidate -> candidate.Name = "UnusedLocalVariable")

        Assert.Empty(rule.Check analyzed selection)

    [<Fact>]
    let ``type methods count each member definition once`` () =
        let analyzed =
            analyzeSource
                """module TestMethodCounts

type Service() =
    member _.M1() = 1
    member _.M2() = 2
"""

        Assert.Equal(2, (Map.find "Service" analyzed.TypeMethods |> List.length))

    [<Fact>]
    let ``nested local functions are excluded from type method counts`` () =
        let analyzed =
            analyzeSource
                """module TestNestedTypeFunctions

type Service =
    member _.Run() =
        let helper value = value + 1
        helper 1
"""

        let selection =
            { Name = "TooManyMethods"
              RulesetName = "test"
              Priority = 3
              Properties = Map.ofList [ "maxmethods", "1" ] }

        let rule = Rules.all |> List.find (fun item -> item.Name = "TooManyMethods")

        Assert.Empty(rule.Check analyzed selection)
        Assert.Equal(1, (Map.find "Service" analyzed.TypeMethods |> List.length))

    [<Fact>]
    let ``nested local functions are excluded from module method counts`` () =
        let analyzed =
            analyzeSource
                """module TestNestedModuleFunctions

let outer value =
    let helper x = x + 1
    helper value
"""

        let selection =
            { Name = "TooManyMethods"
              RulesetName = "test"
              Priority = 3
              Properties = Map.ofList [ "maxmethods", "1" ] }

        let rule = Rules.all |> List.find (fun item -> item.Name = "TooManyMethods")

        Assert.Empty(rule.Check analyzed selection)
        Assert.Equal(1, (Map.find "TestNestedModuleFunctions" analyzed.TypeMethods |> List.length))

    [<Fact>]
    let ``private members called by other members are not flagged unused`` () =
        let analyzed =
            analyzeSource
                """module TestUsedPrivate

type Service() =
    member private this.Helper() = 42
    member this.Run() = this.Helper()
"""

        let selection =
            { Name = "UnusedPrivateMethod"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "UnusedPrivateMethod")
        Assert.Empty(rule.Check analyzed selection)

    [<Fact>]
    let ``genuinely unused private members are still flagged`` () =
        let analyzed =
            analyzeSource
                """module TestUnusedPrivate

type Service() =
    member private this.Helper() = 42
    member this.Run() = 1
"""

        let selection =
            { Name = "UnusedPrivateMethod"
              RulesetName = "unusedcode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "UnusedPrivateMethod")
        let violations = rule.Check analyzed selection

        Assert.Contains(
            violations,
            fun violation -> violation.Description.Contains("'Helper'", StringComparison.Ordinal)
        )

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
    let ``lack of cohesion ignores field names in comments and strings`` () =
        let result =
            Engine.run
                "0.1.0"
                { Defaults.analysisOptions with
                    Paths = [ fixture "issue-64-lack-of-cohesion-comments.fs" ]
                    Rulesets = [ "design" ]
                    Format = Json
                    Only = [ "LackOfCohesionOfMethods" ] }

        Assert.Empty(result.Report.Errors)
        Assert.Equal(2, result.ExitCode)

        let violations =
            result.Report.Violations
            |> List.filter (fun item -> item.RuleName = "LackOfCohesionOfMethods")

        Assert.Single(violations) |> ignore
        Assert.Equal(Some "Service", violations.Head.Context.Type)
        Assert.Equal("Type methods form 2 cohesion groups.", violations.Head.Description)

    [<Fact>]
    let ``declaration scanning ignores multiline strings and block comments`` () =
        let result =
            Engine.run
                "0.1.0"
                { Defaults.analysisOptions with
                    Paths = [ fixture "issue-65-non-code-text-declarations.fs" ]
                    Rulesets = [ "controversial" ]
                    Format = Json }

        Assert.Empty(result.Report.Errors)
        Assert.Empty(result.Report.Violations)
        Assert.Equal(0, result.ExitCode)

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
    let ``ignore-tests does not skip production files when an ancestor directory ends in Tests`` () =
        let workspace = Directory.CreateTempSubdirectory("messfsharp-ancestor-")

        try
            let projectTests =
                Directory.CreateDirectory(Path.Combine(workspace.FullName, "ProjectTests"))

            let src = Directory.CreateDirectory(Path.Combine(projectTests.FullName, "src"))
            let app = Path.Combine(src.FullName, "App.fs")
            File.WriteAllText(app, "module App\nlet value = 1\n")

            let discovered, errors =
                Discovery.discover
                    { options [ src.FullName ] [ "fsharp" ] Text with
                        IgnoreTests = true }

            Assert.Empty(errors)
            Assert.Equal<string list>([ Path.GetFullPath(app) ], discovered)

            let fromNamedRoot, namedRootErrors =
                Discovery.discover
                    { options [ projectTests.FullName ] [ "fsharp" ] Text with
                        IgnoreTests = true }

            Assert.Empty(namedRootErrors)
            Assert.Equal<string list>([ Path.GetFullPath(app) ], fromNamedRoot)
        finally
            workspace.Delete(true)

    [<Fact>]
    let ``ignore-tests still skips nested test directories and test-named files`` () =
        let workspace = Directory.CreateTempSubdirectory("messfsharp-ignore-tests-")

        try
            let src = Directory.CreateDirectory(Path.Combine(workspace.FullName, "src"))
            let app = Path.Combine(src.FullName, "App.fs")
            File.WriteAllText(app, "module App\nlet value = 1\n")

            let unitTests = Directory.CreateDirectory(Path.Combine(src.FullName, "UnitTests"))

            File.WriteAllText(Path.Combine(unitTests.FullName, "Inside.fs"), "module Inside")

            let sampleTests =
                Directory.CreateDirectory(Path.Combine(src.FullName, "Sample.Tests"))

            File.WriteAllText(Path.Combine(sampleTests.FullName, "Inside.fs"), "module SampleInside")

            File.WriteAllText(Path.Combine(src.FullName, "AppTests.fs"), "module AppTests")
            File.WriteAllText(Path.Combine(src.FullName, "FooTest.fs"), "module FooTest")
            File.WriteAllText(Path.Combine(src.FullName, "ScriptTests.fsx"), "module ScriptTests")
            File.WriteAllText(Path.Combine(src.FullName, "ScriptTest.fsx"), "module ScriptTest")

            let discovered, errors =
                Discovery.discover
                    { options [ src.FullName ] [ "fsharp" ] Text with
                        IgnoreTests = true }

            Assert.Empty(errors)
            Assert.Equal<string list>([ Path.GetFullPath(app) ], discovered)
        finally
            workspace.Delete(true)

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
    let ``multiline primary constructors contribute their parameter groups`` () =
        let workspace = Directory.CreateTempSubdirectory("messfsharp-issue-62-")

        try
            let sourcePath = Path.Combine(workspace.FullName, "issue-62.fs")

            File.WriteAllText(
                sourcePath,
                """module MultilineConstructor

type Service
    (
        first: int,
        second: int
    ) =
    member _.Run() = first + second
"""
            )

            let result =
                Engine.run "0.1.0" (options [ sourcePath ] [ fixture "constructor-ruleset.xml" ] Json)

            Assert.Empty(result.Report.Errors)

            Assert.Contains(
                result.Report.Violations,
                fun violation ->
                    violation.RuleName = "ExcessiveParameterList"
                    && violation.Description = "Parameter count 2 exceeds maximum 1."
            )
        finally
            workspace.Delete(true)

    [<Fact>]
    let ``type extensions do not contribute primary constructors`` () =
        let analyzed =
            analyzeSource
                """module TypeExtensions

type Service = class end

type Service with
    member _.Run() = 42
"""

        Assert.DoesNotContain(analyzed.Declarations, fun declaration -> declaration.Kind = Constructor)

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
    let ``exit expressions distinguish definitions member calls and idiomatic process exits`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "issue-33-exit-cases.fs" ] [ fixture "exit-ruleset.xml" ] Json)

        Assert.Empty(result.Report.Errors)

        let exitViolations =
            result.Report.Violations
            |> List.filter (fun violation -> violation.RuleName = "ExitExpression")

        Assert.Equal<int list>(
            [ 14; 15; 16; 17 ],
            exitViolations |> List.map (fun violation -> violation.Location.StartLine)
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
    let ``pattern match cases returning unit after try are not reported as empty handlers`` () =
        let analyzed =
            analyzeSource
                """module Sample

let run status =
    try
        printfn "working"
    finally
        printfn "cleaning"

    match status
    with
    | None -> ()
    | Some value -> printfn "%A" value
"""

        let selection =
            { Name = "EmptyCatchBlock"
              RulesetName = "design"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "EmptyCatchBlock")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``pattern match cases returning unit after try with are not reported as empty handlers`` () =
        let analyzed =
            analyzeSource
                """module Sample

let run status =
    try
        printfn "working"
    with
    | _ -> printfn "failed"

    match status
    with
    | None -> ()
    | Some value -> printfn "%A" value
"""

        let selection =
            { Name = "EmptyCatchBlock"
              RulesetName = "design"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "EmptyCatchBlock")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``unit match clauses nested inside try bodies are not reported as empty handlers`` () =
        let analyzed =
            analyzeSource
                """module Sample

let run status =
    try
        match status
        with
        | None -> ()
        | Some value -> printfn "%A" value
    with
    | _ -> ()
"""

        let selection =
            { Name = "EmptyCatchBlock"
              RulesetName = "design"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "EmptyCatchBlock")
        let violations = rule.Check analyzed selection
        Assert.Single(violations) |> ignore
        Assert.Equal(10, violations[0].Location.StartLine)

    [<Fact>]
    let ``unit exception handler clauses inside try with are still reported`` () =
        let analyzed =
            analyzeSource
                """module Sample

let run status =
    try
        printfn "working"
    with
    | _ -> ()
"""

        let selection =
            { Name = "EmptyCatchBlock"
              RulesetName = "design"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "EmptyCatchBlock")
        let violations = rule.Check analyzed selection

        Assert.Contains(violations, fun violation -> violation.Location.StartLine = 7)

    [<Fact>]
    let ``empty catch block reports handlers whose body is only unit after a block comment`` () =
        let result =
            Engine.run
                "0.1.0"
                { Defaults.analysisOptions with
                    Paths = [ fixture "issue-70-empty-catch-block-comment.fs" ]
                    Rulesets = [ "design" ]
                    Format = Json
                    Only = [ "EmptyCatchBlock" ] }

        Assert.Empty(result.Report.Errors)
        Assert.Equal(2, result.ExitCode)

        let catchLines =
            result.Report.Violations
            |> List.filter (fun violation -> violation.RuleName = "EmptyCatchBlock")
            |> List.map (fun violation -> violation.Location.StartLine)

        Assert.Equal<int list>([ 6; 15; 22 ], catchLines)

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
    let ``extended interpolated string holes are scanned and referenced bindings are not unused`` () =
        let sourceText =
            "module TestExtendedInterpolated\n"
            + "let greet (name: string) =\n"
            + "    let prefix = \"Hello\"\n"
            + "    printfn $$\"\"\"{{prefix}} {{name}}\"\"\"\n"

        let analyzed = analyzeSource sourceText

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

        // Acceptance criteria: Identifiers inside extended interpolation holes are counted as references by referenceCountFor
        Assert.True(analyzed.ReferenceCounts.["prefix"] >= 1)
        Assert.True(analyzed.ReferenceCounts.["name"] >= 1)

    [<Fact>]
    let ``scanner recognizes extended interpolation identifiers in triple-quoted and single-quoted strings`` () =
        let sourceTriple =
            { FullPath = "test.fs"
              Kind = Implementation
              Text = "let _ = $$\"\"\"{{prefix}} {{name}}\"\"\""
              Lines = [| "let _ = $$\"\"\"{{prefix}} {{name}}\"\"\"" |] }

        let tokensTriple = Scanner.scan sourceTriple

        let idTokensTriple =
            tokensTriple
            |> Array.filter (fun t -> t.Kind = Identifier)
            |> Array.map (fun t -> t.Text)

        Assert.Contains("prefix", idTokensTriple)
        Assert.Contains("name", idTokensTriple)

        let sourceSingle =
            { FullPath = "test.fs"
              Kind = Implementation
              Text = "let _ = $$\"{{prefix}} {{name}}\""
              Lines = [| "let _ = $$\"{{prefix}} {{name}}\"" |] }

        let tokensSingle = Scanner.scan sourceSingle

        let idTokensSingle =
            tokensSingle
            |> Array.filter (fun t -> t.Kind = Identifier)
            |> Array.map (fun t -> t.Text)

        Assert.Contains("prefix", idTokensSingle)
        Assert.Contains("name", idTokensSingle)

    [<Fact>]
    let ``scanner preserves single braces in extended interpolation literal text`` () =
        let source =
            { FullPath = "test.fs"
              Kind = Implementation
              Text = "let json = $$\"\"\"{ \"title\": {{title}}, \"count\": 42 }\"\"\""
              Lines = [| "let json = $$\"\"\"{ \"title\": {{title}}, \"count\": 42 }\"\"\"" |] }

        let tokens = Scanner.scan source

        let idTokens =
            tokens
            |> Array.filter (fun t -> t.Kind = Identifier)
            |> Array.map (fun t -> t.Text)

        Assert.Contains("title", idTokens)
        Assert.DoesNotContain("count", idTokens)

    [<Fact>]
    let ``scanner handles triple dollar extended interpolation and verbatim prefixes`` () =
        let sourceReal3 =
            { FullPath = "test.fs"
              Kind = Implementation
              Text = "let _ = $$$ \"\"\"".Replace(" ", "") + "{{{payload}}}\"\"\""
              Lines = [| "let _ = $$$ \"\"\"".Replace(" ", "") + "{{{payload}}}\"\"\"" |] }

        let tokens3 = Scanner.scan sourceReal3

        let idTokens3 =
            tokens3
            |> Array.filter (fun t -> t.Kind = Identifier)
            |> Array.map (fun t -> t.Text)

        Assert.Contains("payload", idTokens3)

        let sourceVerbatimDollar =
            { FullPath = "test.fs"
              Kind = Implementation
              Text = "let _ = @$$\"{{path}}\""
              Lines = [| "let _ = @$$\"{{path}}\"" |] }

        let tokensV = Scanner.scan sourceVerbatimDollar

        let idTokensV =
            tokensV
            |> Array.filter (fun t -> t.Kind = Identifier)
            |> Array.map (fun t -> t.Text)

        Assert.Contains("path", idTokensV)

        let sourceDollarVerbatim =
            { FullPath = "test.fs"
              Kind = Implementation
              Text = "let _ = $$@\"{{dir}}\""
              Lines = [| "let _ = $$@\"{{dir}}\"" |] }

        let tokensDV = Scanner.scan sourceDollarVerbatim

        let idTokensDV =
            tokensDV
            |> Array.filter (fun t -> t.Kind = Identifier)
            |> Array.map (fun t -> t.Text)

        Assert.Contains("dir", idTokensDV)

    [<Fact>]
    let ``scanner scans unicode hex and decimal character literals as CharacterLiteral`` () =
        let source =
            { FullPath = "test.fs"
              Kind = Implementation
              Text =
                "let a = '\\u0041'\nlet b = '\\U00000041'\nlet c = '\\xFF'\nlet d = '\\123'\nlet e = '\\n'\nlet f = 'z'\nlet g = '\\\''\nlet h = '\\\\'"
              Lines =
                [| "let a = '\\u0041'"
                   "let b = '\\U00000041'"
                   "let c = '\\xFF'"
                   "let d = '\\123'"
                   "let e = '\\n'"
                   "let f = 'z'"
                   "let g = '\\\''"
                   "let h = '\\\\'" |] }

        let tokens = Scanner.scan source

        let charTokens =
            tokens
            |> Array.filter (fun t -> t.Kind = CharacterLiteral)
            |> Array.map (fun t -> t.Text)

        Assert.Equal<string[]>(
            [| "'\\u0041'"
               "'\\U00000041'"
               "'\\xFF'"
               "'\\123'"
               "'\\n'"
               "'z'"
               "'\\\''"
               "'\\\\'" |],
            charTokens
        )

        let bogusTokens =
            tokens
            |> Array.filter (fun t -> t.Text = "\\" || (t.Text.Contains("'") && t.Kind <> CharacterLiteral))
            |> Array.map (fun t -> sprintf "%A:%s" t.Kind t.Text)

        Assert.Empty(bogusTokens)

    [<Fact>]
    let ``scanner continues to distinguish generic type parameters from character literals`` () =
        let source =
            { FullPath = "test.fs"
              Kind = Implementation
              Text = "type Container<'T, 'Item> = { Value: 'T; Other: 'Item }"
              Lines = [| "type Container<'T, 'Item> = { Value: 'T; Other: 'Item }" |] }

        let tokens = Scanner.scan source

        let idTokens =
            tokens
            |> Array.filter (fun t -> t.Kind = Identifier)
            |> Array.map (fun t -> t.Text)

        Assert.Contains("'T", idTokens)
        Assert.Contains("'Item", idTokens)

        let charTokens = tokens |> Array.filter (fun t -> t.Kind = CharacterLiteral)

        Assert.Empty(charTokens)

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
    let ``duplicated array key ignores multiline tuple values in single entry map`` () =
        let analyzed =
            analyzeSource
                """module TestMap
let myMap =
    Map.ofList
        [ 1,
          (1, 2) ]
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
    let ``duplicated array key ignores semicolons in string literals`` () =
        let analyzed =
            analyzeSource
                """module TestMap
let myMap =
    Map.ofList
        [ 1, "item; 1, other" ]
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
    let ``duplicated array key detects genuine duplicate keys in list literal`` () =
        let analyzed =
            analyzeSource
                """module TestMap
let myMap = Map.ofList [ "a", 1; "a", 2 ]
"""

        let selection =
            { Name = "DuplicatedArrayKey"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "DuplicatedArrayKey")
        let violations = rule.Check analyzed selection
        Assert.Single(violations) |> ignore

    [<Fact>]
    let ``duplicated array key detects genuine duplicate keys across lines`` () =
        let analyzed =
            analyzeSource
                """module TestMap
let myMap =
    Map.ofList
        [ "a", 1
          "a", 2 ]
"""

        let selection =
            { Name = "DuplicatedArrayKey"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "DuplicatedArrayKey")
        let violations = rule.Check analyzed selection
        Assert.Single(violations) |> ignore

    [<Fact>]
    let ``duplicated array key keeps entries after nested brackets`` () =
        let analyzed =
            analyzeSource
                """module TestMap
let nestedList =
    Map.ofList
        [ "dup", [ 1 ]
          "dup", [ 2 ] ]

let nestedArray =
    Map.ofArray
        [| "dup", [| 1 |]
           "dup", [| 2 |] |]

let indexedValue =
    let values = [| 1; 2 |]
    Map.ofList
        [ "dup", values.[0]
          "dup", values.[1] ]

let dictionary =
    dict
        [ "dup", [| 1 |]
          "dup", [| 2 |] ]
"""

        let selection =
            { Name = "DuplicatedArrayKey"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "DuplicatedArrayKey")
        let violations = rule.Check analyzed selection

        Assert.Equal(4, violations.Length)

        Assert.Equal<int list>(
            [ 3; 8; 14; 19 ],
            violations
            |> List.map (fun violation -> violation.Location.StartLine)
            |> List.sort
        )

    [<Fact>]
    let ``duplicated array key ignores nested collections containing semicolons`` () =
        let analyzed =
            analyzeSource
                """module TestMap
let myMap =
    Map.ofList
        [ 1, [ "first; nested"; "second" ]
          2, [ "third; item" ] ]
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
    let ``duplicated array key detects duplicate KeyValuePair entries in Dictionary constructor`` () =
        let analyzed =
            analyzeSource
                """module TestDictionary
open System.Collections.Generic

let duplicates =
    Dictionary<string, int>(
        [ KeyValuePair<string, int>("same", 1)
          KeyValuePair<string, int>("same", 2) ])
"""

        let selection =
            { Name = "DuplicatedArrayKey"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "DuplicatedArrayKey")
        let violations = rule.Check analyzed selection
        Assert.Single(violations) |> ignore

    [<Fact>]
    let ``duplicated array key detects duplicate KeyValuePair entries in qualified Dictionary constructor`` () =
        let analyzed =
            analyzeSource
                """module TestDictionary
open System.Collections.Generic

let duplicates =
    System.Collections.Generic.Dictionary<string, int>(
        [ System.Collections.Generic.KeyValuePair<string, int>("same", 1)
          System.Collections.Generic.KeyValuePair<string, int>("same", 2) ])
"""

        let selection =
            { Name = "DuplicatedArrayKey"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "DuplicatedArrayKey")
        let violations = rule.Check analyzed selection
        Assert.Single(violations) |> ignore

    [<Fact>]
    let ``duplicated array key ignores distinct KeyValuePair entries in Dictionary constructor`` () =
        let analyzed =
            analyzeSource
                """module TestDictionary
open System.Collections.Generic

let distinct =
    Dictionary<string, int>(
        [ KeyValuePair<string, int>("first", 1)
          KeyValuePair<string, int>("second", 2) ])
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
    let ``static access does not flag namespace declarations`` () =
        let analyzed =
            analyzeSource
                """namespace System.Collections.Specialized

module Helpers =
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
    let ``static access does not flag module declarations`` () =
        let analyzed =
            analyzeSource
                """module Microsoft.FSharp.Utilities

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
    let ``static access does not flag type annotations on parameters return types and bindings`` () =
        let analyzed =
            analyzeSource
                """module TestAnnotations
let processItems (items: System.Collections.Generic.List<int>) = items.Count
let loadReader () : System.IO.StreamReader = failwith "no"
let cache: System.Collections.Concurrent.ConcurrentDictionary<string, int> = failwith "no"
let nested: System.Collections.Generic.List<System.Collections.Generic.Queue<int>> = failwith "no"
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
    let ``static access does not flag attribute applications`` () =
        let analyzed =
            analyzeSource
                """module TestAttributes
[<System.Diagnostics.CodeAnalysis.SuppressMessage("Category", "CheckId")>]
let suppressed () = 42

[<System.Diagnostics.CodeAnalysis.SuppressMessage("Category", "CheckId")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("Category", "OtherCheckId")>]
let alsoSuppressed () = 43
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
    let ``static access suppression attributes do not leave unsuppressable violations`` () =
        let result =
            Engine.run "0.1.0" (options [ fixture "static-access-suppressed.fs" ] [ "cleancode" ] Json)

        Assert.Empty(result.Report.Errors)

        Assert.DoesNotContain(result.Report.Violations, fun violation -> violation.RuleName = "StaticAccess")

    [<Fact>]
    let ``static access still flags static member and property invocations`` () =
        let analyzed =
            analyzeSource
                """module TestStaticAccess
let now () = System.DateTime.UtcNow
let read () = System.IO.File.ReadAllText("input.txt")
let annotated (value: int) = value
"""

        let selection =
            { Name = "StaticAccess"
              RulesetName = "cleancode"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "StaticAccess")
        let violations = rule.Check analyzed selection
        Assert.Equal(2, violations |> List.length)

        Assert.Equal(
            2,
            violations
            |> List.map (fun violation -> violation.Location.StartLine)
            |> List.distinct
            |> List.length
        )

    [<Fact>]
    let ``boolean argument flag does not treat if text in comments and strings as control flow`` () =
        let result =
            Engine.run
                "0.1.0"
                { Defaults.analysisOptions with
                    Paths = [ fixture "issue-63-boolean-if-text.fs" ]
                    Rulesets = [ "cleancode" ]
                    Format = Json
                    Only = [ "BooleanArgumentFlag" ] }

        Assert.Empty(result.Report.Errors)

        let flagLines =
            result.Report.Violations
            |> List.filter (fun violation -> violation.RuleName = "BooleanArgumentFlag")
            |> List.map (fun violation -> violation.Location.StartLine)

        Assert.Equal<int list>([ 15 ], flagLines)

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
    let ``else expression does not treat string literals or comments as terminating`` () =
        let result =
            Engine.run
                "0.1.0"
                { Defaults.analysisOptions with
                    Paths = [ fixture "issue-34-else-string.fs" ]
                    Rulesets = [ "cleancode" ]
                    Format = Json
                    Only = [ "ElseExpression" ] }

        Assert.Empty(result.Report.Errors)

        let elseLines =
            result.Report.Violations
            |> List.filter (fun violation -> violation.RuleName = "ElseExpression")
            |> List.map (fun violation -> violation.Location.StartLine)

        Assert.Equal<int list>([ 39; 46; 53; 56 ], elseLines)

    [<Fact>]
    let ``attributes on the declaration line itself are collected`` () =
        // F# only allows same-line attributes on module-level let bindings when
        // nothing follows them in the module body, so each case is a minimal source.
        let literalDeclaration =
            analyzeSource
                """module TestSameLineLiteral
[<Literal>] let MAX_SIZE = 10
"""

        Assert.True(
            (literalDeclaration.Declarations
             |> List.find (fun declaration -> declaration.Name = "MAX_SIZE"))
                .IsLiteral
        )

        let compilerGeneratedDeclaration =
            analyzeSource
                """module TestSameLineCompilerGenerated
[<CompilerGenerated>] let generated = 2
"""

        Assert.True(
            (compilerGeneratedDeclaration.Declarations
             |> List.find (fun declaration -> declaration.Name = "generated"))
                .IsCompilerGenerated
        )

        let suppressedDeclaration =
            analyzeSource
                """module TestSameLineSuppression
open System.Diagnostics.CodeAnalysis

type Handler() =
    [<SuppressMessage("messfsharp", "LongMethod")>] member this.SuppressedMethod() = 1
    member this.PlainMethod() = 2
"""

        Assert.Equal<string>(
            Set.ofList [ "LongMethod" ],
            (suppressedDeclaration.Declarations
             |> List.find (fun declaration -> declaration.Name = "SuppressedMethod"))
                .SuppressedRules
        )

        let multilineDeclaration =
            analyzeSource
                """module TestMultilineAttributes
open System.Diagnostics.CodeAnalysis

[<SuppressMessage("messfsharp", "ShortVariable")>]
[<Literal>]
let bothLines = 3
"""

        let bothLines =
            multilineDeclaration.Declarations
            |> List.find (fun declaration -> declaration.Name = "bothLines")

        Assert.Equal<string>(Set.ofList [ "ShortVariable" ], bothLines.SuppressedRules)
        Assert.True(bothLines.IsLiteral)

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
    let ``suppress message on the declaration line itself suppresses rule violation`` () =
        let source =
            """module TestSameLineSuppression
open System.Diagnostics.CodeAnalysis

[<SuppressMessage("messfsharp", "ShortVariable")>] let v = 1
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

    [<Fact>]
    let ``coupling between objects excludes comments string literals and own members`` () =
        let source =
            """module TestCoupling

type SimpleService() =
    // Note: Alpha Bravo Charlie Delta Echo Foxtrot Golf Hotel India Juliet Kilo Lima Mike
    (* MultiLine Doc: One Two Three Four Five Six Seven Eight Nine Ten Eleven Twelve *)
    let message = "November Oscar Papa Quebec Romeo Sierra Tango Uniform Victor Whiskey Xray Yankee Zulu"
    member this.FirstMethod() = message
    member this.SecondMethod() = ()
    member val FirstProperty = 1 with get, set
"""

        let analyzed = analyzeSource source

        let selection =
            { Name = "CouplingBetweenObjects"
              RulesetName = "design"
              Priority = 3
              Properties = Map.ofList [ "maximum", "13" ] }

        let rule = Rules.all |> List.find (fun r -> r.Name = "CouplingBetweenObjects")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``coupling between objects counts genuine external types and triggers when exceeding threshold`` () =
        let source =
            """module TestExternalCoupling

type ComplexService(
    dep1: IAlpha,
    dep2: IBravo,
    dep3: ICharlie,
    dep4: IDelta,
    dep5: IEcho,
    dep6: IFoxtrot,
    dep7: IGolf,
    dep8: IHotel,
    dep9: IIndia,
    dep10: IJuliet,
    dep11: IKilo,
    dep12: ILima,
    dep13: IMike,
    dep14: INovember) =

    member this.DoWork() = ()
"""

        let analyzed = analyzeSource source

        let selection =
            { Name = "CouplingBetweenObjects"
              RulesetName = "design"
              Priority = 3
              Properties = Map.ofList [ "maximum", "13" ] }

        let rule = Rules.all |> List.find (fun r -> r.Name = "CouplingBetweenObjects")
        let violations = rule.Check analyzed selection
        Assert.Single(violations) |> ignore
        let v = violations.Head
        Assert.Equal("Coupling count 14 exceeds maximum 13.", v.Description)

    [<Fact>]
    let ``coupling between objects excludes built in primitives and fsharp core types`` () =
        let source =
            """module TestBuiltins

type BuiltinConsumer(
    a: int,
    b: string,
    c: bool,
    d: obj,
    e: char,
    f: byte,
    g: int16,
    h: int64,
    i: float,
    j: decimal,
    k: System.DateTime,
    l: System.Guid,
    m: System.TimeSpan,
    n: System.Exception,
    o: Option<int>,
    p: Result<int, string>,
    q: int list) =

    member this.Process() =
        let opt = Some 42
        let none = None
        let res = Ok "success"
        let err = Error "failure"
        let list = [ 1; 2; 3 ]
        let arr = [| 1; 2 |]
        let m = Map.empty<string, int>
        let s = Set.empty<int>
        ()
"""

        let analyzed = analyzeSource source

        let selection =
            { Name = "CouplingBetweenObjects"
              RulesetName = "design"
              Priority = 3
              Properties = Map.ofList [ "maximum", "0" ] }

        let rule = Rules.all |> List.find (fun r -> r.Name = "CouplingBetweenObjects")
        let violations = rule.Check analyzed selection
        Assert.Empty(violations)

    [<Fact>]
    let ``boolean get method name does not flag non-boolean methods containing boolean literals or comments`` () =
        let analyzed =
            analyzeSource
                """module TestBooleanMethods
type Service =
    // Returns timeout in ms. Default is true in legacy config.
    member this.GetTimeout() : int =
        let useDefault = true
        if useDefault then 5000 else 1000

    member this.GetMessage() : string =
        "true"

    member this.GetTimeoutWithParam(flag: bool) : int =
        if flag then 5000 else 1000

    // Genuine boolean methods should still be flagged
    member this.GetIsValid() : bool =
        true

    member this.GetHasPermission() : Boolean =
        true

    abstract member GetEnabled: unit -> bool
"""

        let timeoutDecl =
            analyzed.Declarations |> List.find (fun d -> d.Name = "GetTimeout")

        let messageDecl =
            analyzed.Declarations |> List.find (fun d -> d.Name = "GetMessage")

        let paramDecl =
            analyzed.Declarations |> List.find (fun d -> d.Name = "GetTimeoutWithParam")

        let validDecl = analyzed.Declarations |> List.find (fun d -> d.Name = "GetIsValid")

        let permissionDecl =
            analyzed.Declarations |> List.find (fun d -> d.Name = "GetHasPermission")

        let enabledDecl =
            analyzed.Declarations |> List.find (fun d -> d.Name = "GetEnabled")

        Assert.False(timeoutDecl.IsBoolean)
        Assert.False(messageDecl.IsBoolean)
        Assert.False(paramDecl.IsBoolean)
        Assert.True(validDecl.IsBoolean)
        Assert.True(permissionDecl.IsBoolean)
        Assert.True(enabledDecl.IsBoolean)

        let selection =
            { Name = "BooleanGetMethodName"
              RulesetName = "fsharp"
              Priority = 3
              Properties = Map.empty }

        let rule = Rules.all |> List.find (fun r -> r.Name = "BooleanGetMethodName")
        let violations = rule.Check analyzed selection

        let violationNames =
            violations
            |> List.choose (fun v ->
                // extract member name from "Boolean member '...' should use..."
                let m = System.Text.RegularExpressions.Regex.Match(v.Description, "'([^']+)'")
                if m.Success then Some m.Groups[1].Value else None)

        Assert.DoesNotContain("GetTimeout", violationNames)
        Assert.DoesNotContain("GetMessage", violationNames)
        Assert.DoesNotContain("GetTimeoutWithParam", violationNames)
        Assert.Contains("GetIsValid", violationNames)
        Assert.Contains("GetHasPermission", violationNames)
        Assert.Contains("GetEnabled", violationNames)

    [<Fact>]
    let ``boolean get method name does not count property getter accessors as parameters`` () =
        let analyzed =
            analyzeSource
                """module Sample

type Service() =
    member _.GetReady with get() = true
    member _.Item with get(index: int) = index
    member _.GetValue(``get``: int) = true
"""

        let declaration name =
            analyzed.Declarations |> List.find (fun item -> item.Name = name)

        let getReady = declaration "GetReady"
        let item = declaration "Item"
        let getValue = declaration "GetValue"

        Assert.Equal(Property, getReady.Kind)
        Assert.Equal(0, getReady.ParameterCount)
        Assert.Equal(Property, item.Kind)
        Assert.Equal(1, item.ParameterCount)
        Assert.Equal(Member, getValue.Kind)
        Assert.Equal(1, getValue.ParameterCount)

        let selection properties =
            { Name = "BooleanGetMethodName"
              RulesetName = "naming"
              Priority = 3
              Properties = properties }

        let rule =
            Rules.all
            |> List.find (fun candidate -> candidate.Name = "BooleanGetMethodName")

        let violationNames properties =
            rule.Check analyzed (selection properties)
            |> List.map (fun violation ->
                let matchResult =
                    System.Text.RegularExpressions.Regex.Match(violation.Description, "'([^']+)'")

                matchResult.Groups[1].Value)

        let defaultNames = violationNames Map.empty

        let disabledNames =
            violationNames (Map.ofList [ "checkParameterizedMethods", "false" ])

        Assert.Contains("GetReady", defaultNames)
        Assert.Contains("GetValue", defaultNames)
        Assert.DoesNotContain("Item", defaultNames)
        Assert.Contains("GetReady", disabledNames)
        Assert.DoesNotContain("GetValue", disabledNames)
        Assert.DoesNotContain("Item", disabledNames)

    [<Fact>]
    let ``mutually recursive and let-bindings are distinct declarations with independent metrics`` () =
        let analyzed =
            analyzeSource
                """module Sample

let rec f x =
    if x > 0 then
        g (x - 1)
    else
        0
and g y =
    if y <= 0 then
        0
    else
        f y
"""

        let functions =
            analyzed.Declarations
            |> List.filter (fun declaration -> declaration.Kind = Function)

        let f = functions |> List.find (fun declaration -> declaration.Name = "f")
        let g = functions |> List.find (fun declaration -> declaration.Name = "g")

        Assert.Equal(3, f.Location.StartLine)
        Assert.Equal(7, f.Location.EndLine)
        Assert.Equal(8, g.Location.StartLine)
        Assert.Equal(13, g.Location.EndLine)
        Assert.True(f.Location.EndLine < g.Location.StartLine)
        Assert.Contains("g (x - 1)", f.Text)
        Assert.DoesNotContain("and g y", f.Text)
        Assert.Contains("if y <= 0 then", g.Text)
        Assert.Contains("f y", g.Text)
        Assert.Equal(1, f.ParameterCount)
        Assert.Equal(1, g.ParameterCount)

        let metric (map: Map<string * int, int>) (declaration: Declaration) =
            Map.tryFind (declaration.Name, declaration.Location.StartLine) map
            |> Option.defaultValue 0

        Assert.Equal(2, metric analyzed.ComplexityByDeclaration f)
        Assert.Equal(2, metric analyzed.ComplexityByDeclaration g)
        Assert.Equal(2, metric analyzed.NPathByDeclaration f)
        Assert.Equal(2, metric analyzed.NPathByDeclaration g)
        Assert.Equal(5, metric analyzed.LineCountByDeclaration f)
        Assert.Equal(6, metric analyzed.LineCountByDeclaration g)

        let check ruleName properties =
            let rule = Rules.all |> List.find (fun rule -> rule.Name = ruleName)

            rule.Check
                analyzed
                { Name = ruleName
                  RulesetName = "codesize"
                  Priority = 3
                  Properties = properties }

        let startLines violations =
            violations
            |> List.map (fun violation -> violation.Location.StartLine)
            |> List.sort

        Assert.Equal<int list>([ 3; 8 ], startLines (check "CyclomaticComplexity" (Map.ofList [ "maximum", "1" ])))
        Assert.Equal<int list>([ 3; 8 ], startLines (check "NPathComplexity" (Map.ofList [ "maximum", "1" ])))
        Assert.Equal<int list>([ 3; 8 ], startLines (check "ExcessiveMethodLength" (Map.ofList [ "minimum", "4" ])))

    [<Fact>]
    let ``cyclomatic complexity ignores literal delimiters but counts pattern cases`` () =
        let analyzed =
            analyzeSource
                """module Literals

let arrays () =
    [| 1; 2 |]

let anonymousRecord () =
    {| Value = 1 |}

let matching value =
    match value with
    | Some result -> result
    | None -> 0

let matchingFunction value =
    let convert =
        function
        | Some result -> result
        | None -> 0

    convert value
"""

        let complexity name =
            let declaration =
                analyzed.Declarations
                |> List.find (fun declaration ->
                    declaration.Kind = Function && declaration.Name = name)

            Map.tryFind (declaration.Name, declaration.Location.StartLine) analyzed.ComplexityByDeclaration
            |> Option.defaultValue 0

        let actual =
            [ complexity "arrays"
              complexity "anonymousRecord"
              complexity "matching"
              complexity "matchingFunction" ]

        Assert.Equal<int list>([ 1; 1; 3; 3 ], actual)

    [<Fact>]
    let ``declarations in inactive conditional compilation branches are excluded`` () =
        let analyzed =
            analyzeSource
                """module TestInactiveBranch

#if NEVER_DEFINED
let BadName = 1
#else
let goodName = 2
#endif

let alsoGood = 3
"""

        let named name =
            analyzed.Declarations |> List.filter (fun d -> d.Name = name) |> List.length

        Assert.Equal(0, named "BadName")
        Assert.Equal(1, named "goodName")
        Assert.Equal(1, named "alsoGood")

    [<Fact>]
    let ``child declarations track enclosing parent start line`` () =
        let analyzed =
            analyzeSource
                """namespace Alpha
type Config =
    val mutable A: int

type Service() =
    member _.Run(flag: bool) = flag

namespace Beta
type Config =
    val mutable B: int
"""

        let alphaConfig =
            analyzed.Declarations
            |> List.find (fun d -> d.Kind = Type && d.Name = "Config" && d.Location.StartLine = 2)

        let betaConfig =
            analyzed.Declarations
            |> List.find (fun d -> d.Kind = Type && d.Name = "Config" && d.Location.StartLine = 9)

        let fieldA =
            analyzed.Declarations |> List.find (fun d -> d.Kind = Field && d.Name = "A")

        let fieldB =
            analyzed.Declarations |> List.find (fun d -> d.Kind = Field && d.Name = "B")

        Assert.Equal(Some 2, fieldA.ParentStartLine)
        Assert.Equal(Some 9, fieldB.ParentStartLine)
        Assert.True(isChildOf alphaConfig fieldA)
        Assert.False(isChildOf alphaConfig fieldB)
        Assert.True(isChildOf betaConfig fieldB)
        Assert.False(isChildOf betaConfig fieldA)

    [<Fact>]
    let ``identically named types in different scopes do not cross-inflate fields or methods`` () =
        let analyzed =
            analyzeSource
                """namespace Alpha
type Config =
    val mutable A: int
    val mutable B: int

type Service() =
    member _.One() = 1
    member _.Two() = 2

namespace Beta
type Config =
    val mutable C: int
    val mutable D: int

type Service() =
    member _.Three() = 3
    member _.Four() = 4
"""

        let check ruleName props =
            let selection =
                { Name = ruleName
                  RulesetName = "test"
                  Priority = 3
                  Properties = props }

            let rule = Rules.all |> List.find (fun item -> item.Name = ruleName)
            rule.Check analyzed selection

        Assert.Empty(check "TooManyFields" (Map.ofList [ "maxfields", "2" ]))
        Assert.Empty(check "TooManyMethods" (Map.ofList [ "maxmethods", "3" ]))
        Assert.Empty(check "TooManyPublicMethods" (Map.ofList [ "maxmethods", "3" ]))
        Assert.Empty(check "ExcessiveClassComplexity" (Map.ofList [ "maximum", "2" ]))

    [<Fact>]
    let ``lack of cohesion of methods evaluates cohesion groups strictly within declaration scope`` () =
        let analyzed =
            analyzeSource
                """namespace Alpha

type Service() =
    let sharedAlpha = 0
    member _.First() = sharedAlpha
    member _.Second() = sharedAlpha

namespace Beta

type Service() =
    let sharedBeta = 0
    member _.Third() = sharedBeta
    member _.Fourth() = sharedBeta
"""

        let selection =
            { Name = "LackOfCohesionOfMethods"
              RulesetName = "test"
              Priority = 3
              Properties = Map.ofList [ "minimum", "1" ] }

        let rule =
            Rules.all |> List.find (fun item -> item.Name = "LackOfCohesionOfMethods")

        Assert.Empty(rule.Check analyzed selection)

    [<Fact>]
    let ``coupling between objects calculates own names strictly within target declaration scope`` () =
        let analyzed =
            analyzeSource
                """namespace Alpha
type Service() =
    member _.Target() = 1

namespace Beta
type Service() =
    member _.Run() = Target.Execute()
"""

        let betaService =
            analyzed.Declarations
            |> List.find (fun d -> d.Kind = Type && d.Name = "Service" && d.Location.StartLine = 6)

        let targetMember =
            analyzed.Declarations
            |> List.find (fun d -> d.Kind = Member && d.Name = "Target")

        Assert.False(isChildOf betaService targetMember)
