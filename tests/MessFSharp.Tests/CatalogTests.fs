namespace MessFSharp.Tests

open Xunit
open MessFSharp
open MessFSharp.Domain

module CatalogTests =
    let private components =
        [ "codesize",
          [ "CyclomaticComplexity"
            "NPathComplexity"
            "ExcessiveMethodLength"
            "ExcessiveClassLength"
            "ExcessiveParameterList"
            "ExcessivePublicCount"
            "TooManyFields"
            "TooManyMethods"
            "TooManyPublicMethods"
            "ExcessiveClassComplexity" ]
          "naming",
          [ "ShortClassName"
            "LongClassName"
            "ShortVariable"
            "LongVariable"
            "ShortMethodName"
            "ConstantNamingConventions"
            "BooleanGetMethodName" ]
          "unusedcode",
          [ "UnusedPrivateField"
            "UnusedLocalVariable"
            "UnusedPrivateMethod"
            "UnusedFormalParameter" ]
          "cleancode",
          [ "BooleanArgumentFlag"
            "ElseExpression"
            "StaticAccess"
            "IfStatementAssignment"
            "DuplicatedArrayKey" ]
          "design",
          [ "ExitExpression"
            "GotoStatement"
            "CountInLoopExpression"
            "DevelopmentCodeFragment"
            "EmptyCatchBlock"
            "CouplingBetweenObjects"
            "GlobalVariable"
            "LackOfCohesionOfMethods" ]
          "controversial",
          [ "CamelCaseClassName"
            "CamelCaseMethodName"
            "CamelCasePropertyName"
            "CamelCaseParameterName"
            "CamelCaseVariableName" ]
          "explicitness", [ "ImplicitInput"; "ImplicitOutput" ]
          "strictexplicitness", [ "ImplicitClassInput"; "ImplicitClassOutput" ] ]

    let private loaded name =
        match Rulesets.load [ name ] with
        | Ok value -> value
        | Error errors -> failwithf "Could not load %s: %A" name errors

    let private names (ruleset: Rulesets.Loaded) =
        ruleset.Selections |> List.map (fun selection -> selection.Name)

    [<Fact>]
    let ``component rulesets expose the exact fixed catalog`` () =
        for rulesetName, expectedRules in components do
            Assert.Equal<string list>(expectedRules, loaded rulesetName |> names)

        let allExpected = components |> List.collect snd |> Set.ofList
        let allImplementations = Rules.all |> List.map (fun rule -> rule.Name) |> Set.ofList
        Assert.Equal<Set<string>>(allExpected, allImplementations)

    [<Fact>]
    let ``recommended and opinionated compositions retain their exact contracts`` () =
        let excluded =
            set
                [ "UnusedFormalParameter"
                  "ElseExpression"
                  "BooleanArgumentFlag"
                  "StaticAccess"
                  "ShortVariable"
                  "CountInLoopExpression"
                  "ImplicitInput"
                  "ImplicitOutput"
                  "ImplicitClassInput"
                  "ImplicitClassOutput" ]

        let allRules = components |> List.collect snd |> Set.ofList
        let recommended = loaded "fsharp"
        Assert.Equal<Set<string>>(Set.difference allRules excluded, recommended |> names |> Set.ofList)

        let longVariable =
            recommended.Selections
            |> List.find (fun selection -> selection.Name = "LongVariable")

        Assert.Equal(Some "35", Map.tryFind "maximum" longVariable.Properties)

        Assert.Equal<string list>(
            [ "UnusedFormalParameter"
              "ElseExpression"
              "BooleanArgumentFlag"
              "StaticAccess"
              "ShortVariable"
              "CountInLoopExpression" ],
            loaded "opinionated" |> names
        )

    [<Fact>]
    let ``rule priorities property names and thresholds are stable`` () =
        let implementations =
            Rules.all |> List.map (fun rule -> rule.Name, rule) |> Map.ofList

        for rule in Rules.all do
            let expectedPriority = if rule.Name.StartsWith("CamelCase") then 4 else 3
            Assert.Equal(expectedPriority, rule.DefaultPriority)

        let property rule key =
            implementations[rule].DefaultProperties[key]

        Assert.Equal("10", property "CyclomaticComplexity" "maximum")
        Assert.Equal("200", property "NPathComplexity" "maximum")
        Assert.Equal("100", property "ExcessiveMethodLength" "minimum")
        Assert.Equal("1000", property "ExcessiveClassLength" "minimum")
        Assert.Equal("10", property "ExcessiveParameterList" "maximum")
        Assert.Equal("45", property "ExcessivePublicCount" "maximum")
        Assert.Equal("15", property "TooManyFields" "maxfields")
        Assert.Equal("25", property "TooManyMethods" "maxmethods")
        Assert.Equal("10", property "TooManyPublicMethods" "maxmethods")
        Assert.Equal("50", property "ExcessiveClassComplexity" "maximum")
        Assert.Equal("13", property "CouplingBetweenObjects" "maximum")
        Assert.Equal("1", property "LackOfCohesionOfMethods" "minimum")
        Assert.Equal("PascalCase", property "ConstantNamingConventions" "convention")
        Assert.Equal("true", property "BooleanGetMethodName" "checkParameterizedMethods")
        Assert.Equal("false", property "GlobalVariable" "report-immutable")

        Assert.True(
            implementations["LongVariable"].DefaultProperties.ContainsKey("subtract-prefixes")
            |> not
        )

    [<Fact>]
    let ``custom ruleset property overrides with case insensitive names replace defaults`` () =
        let xml =
            """<?xml version="1.0" encoding="utf-8"?>
<ruleset name="custom">
    <rule ref="BooleanGetMethodName">
        <property name="checkparameterizedmethods" value="false" />
    </rule>
</ruleset>"""

        let tempFile = System.IO.Path.GetTempFileName()

        try
            System.IO.File.WriteAllText(tempFile, xml)
            let loadedRuleset = loaded tempFile

            let selection =
                loadedRuleset.Selections |> List.find (fun s -> s.Name = "BooleanGetMethodName")

            let matchingValue =
                selection.Properties
                |> Seq.tryPick (fun item ->
                    if
                        System.String.Equals(
                            item.Key,
                            "checkParameterizedMethods",
                            System.StringComparison.OrdinalIgnoreCase
                        )
                    then
                        Some item.Value
                    else
                        None)

            Assert.Equal(Some "false", matchingValue)
            Assert.Equal(1, selection.Properties.Count)
        finally
            System.IO.File.Delete(tempFile)

    let private noFilter: RuleFilter =
        { Only = []
          Disable = []
          MinimumPriority = None
          MaximumPriority = None }

    let private filtered filter ruleset =
        match Rulesets.filterSelections filter ruleset with
        | Ok value -> names value
        | Error errors -> failwithf "Could not filter selections: %A" errors

    [<Fact>]
    let ``rule filter rejects a requested rule that no loaded ruleset selects`` () =
        let result =
            Rulesets.filterSelections
                { noFilter with
                    Only = [ "ShortVariable"; "Bogus" ] }
                (loaded "naming")

        Assert.Equal(Error [ "Requested rule 'bogus' is not present in the loaded rulesets." ], result)

    [<Fact>]
    let ``rule filter rejects a disabled rule that no loaded ruleset selects`` () =
        let result =
            Rulesets.filterSelections
                { noFilter with
                    Disable = [ "ShortVariable"; "Bogus" ] }
                (loaded "naming")

        Assert.Equal(Error [ "Disabled rule 'bogus' is not present in the loaded rulesets." ], result)

    [<Fact>]
    let ``rule filter matches requested and disabled rule names without regard to case`` () =
        let filter =
            { noFilter with
                Only = [ "shortvariable"; "LONGVARIABLE"; "ShortMethodName" ]
                Disable = [ "shortMETHODname" ] }

        Assert.Equal<string list>([ "ShortVariable"; "LongVariable" ], filtered filter (loaded "naming"))

    [<Fact>]
    let ``rule filter priority bounds are inclusive`` () =
        let namingAndControversial =
            match Rulesets.load [ "naming"; "controversial" ] with
            | Ok value -> value
            | Error errors -> failwithf "Could not load rulesets: %A" errors

        let namingRules =
            [ "ShortClassName"
              "LongClassName"
              "ShortVariable"
              "LongVariable"
              "ShortMethodName"
              "ConstantNamingConventions"
              "BooleanGetMethodName" ]

        let controversialRules =
            [ "CamelCaseClassName"
              "CamelCaseMethodName"
              "CamelCasePropertyName"
              "CamelCaseParameterName"
              "CamelCaseVariableName" ]

        Assert.Equal<string list>(
            namingRules,
            filtered
                { noFilter with
                    MinimumPriority = Some 3 }
                namingAndControversial
        )

        Assert.Equal<string list>(
            controversialRules,
            filtered
                { noFilter with
                    MaximumPriority = Some 4 }
                namingAndControversial
        )

        Assert.Equal<string list>(
            namingRules @ controversialRules,
            filtered
                { noFilter with
                    MinimumPriority = Some 4
                    MaximumPriority = Some 3 }
                namingAndControversial
        )
