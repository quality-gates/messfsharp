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
            "CamelCaseVariableName" ] ]

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
                  "CountInLoopExpression" ]

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
