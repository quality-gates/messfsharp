namespace MessFSharp

open System
open System.IO
open System.Xml.Linq
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "EmptyCatchBlock")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "GlobalVariable")>]
module Rulesets =
    type Loaded =
        { Selections: RuleSelection list
          Warnings: string list }

    let private componentRulesets =
        [ ("codesize",
           [ "CyclomaticComplexity"
             "NPathComplexity"
             "ExcessiveMethodLength"
             "ExcessiveClassLength"
             "ExcessiveParameterList"
             "ExcessivePublicCount"
             "TooManyFields"
             "TooManyMethods"
             "TooManyPublicMethods"
             "ExcessiveClassComplexity" ])
          ("naming",
           [ "ShortClassName"
             "LongClassName"
             "ShortVariable"
             "LongVariable"
             "ShortMethodName"
             "ConstantNamingConventions"
             "BooleanGetMethodName" ])
          ("unusedcode",
           [ "UnusedPrivateField"
             "UnusedLocalVariable"
             "UnusedPrivateMethod"
             "UnusedFormalParameter" ])
          ("cleancode",
           [ "BooleanArgumentFlag"
             "ElseExpression"
             "StaticAccess"
             "IfStatementAssignment"
             "DuplicatedArrayKey" ])
          ("design",
           [ "ExitExpression"
             "GotoStatement"
             "CountInLoopExpression"
             "DevelopmentCodeFragment"
             "EmptyCatchBlock"
             "CouplingBetweenObjects"
             "GlobalVariable"
             "LackOfCohesionOfMethods" ])
          ("controversial",
           [ "CamelCaseClassName"
             "CamelCaseMethodName"
             "CamelCasePropertyName"
             "CamelCaseParameterName"
             "CamelCaseVariableName" ]) ]

    let private componentMap =
        componentRulesets |> List.map (fun (name, rules) -> name, rules) |> Map.ofList

    let private fsharpExclusions =
        set
            [ "UnusedFormalParameter"
              "ElseExpression"
              "BooleanArgumentFlag"
              "StaticAccess"
              "ShortVariable"
              "LongVariable"
              "CountInLoopExpression" ]

    let private fsharpRules =
        (componentRulesets
         |> List.collect (fun (_, rules) -> rules)
         |> List.filter (fun name -> not (fsharpExclusions.Contains(name))))
        @ [ "LongVariable" ]

    let private opinionatedRules =
        [ "UnusedFormalParameter"
          "ElseExpression"
          "BooleanArgumentFlag"
          "StaticAccess"
          "ShortVariable"
          "CountInLoopExpression" ]

    let private builtInRuleset (name: string) =
        match name.ToLowerInvariant() with
        | "codesize"
        | "naming"
        | "unusedcode"
        | "cleancode"
        | "design"
        | "controversial" ->
            componentMap
            |> Map.tryFind (name.ToLowerInvariant())
            |> Option.map (fun rules -> name.ToLowerInvariant(), rules)
        | "fsharp" -> Some("fsharp", fsharpRules)
        | "opinionated" -> Some("opinionated", opinionatedRules)
        | _ -> None

    let private ruleNameFromText (value: string) =
        let normalized = value.Trim().Replace('\\', '/')
        let lastSlash = normalized.LastIndexOf('/')

        if lastSlash >= 0 then
            let suffix = normalized.Substring(lastSlash + 1)

            if String.IsNullOrWhiteSpace suffix then
                None
            else
                Some suffix
        else
            Some normalized

    let private rulesetNameFromText (value: string) =
        let normalized = value.Trim().Replace('\\', '/')
        let lastSlash = normalized.LastIndexOf('/')

        if
            lastSlash < 0
            && not (normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        then
            None
        else
            let lastPart =
                if lastSlash >= 0 then
                    normalized.Substring(lastSlash + 1)
                else
                    normalized

            let withoutRule =
                if
                    lastSlash >= 0
                    && not (lastPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                then
                    normalized.Substring(0, lastSlash)
                else
                    normalized

            let fileName = Path.GetFileNameWithoutExtension(withoutRule)

            if String.IsNullOrWhiteSpace fileName || fileName = "rulesets" then
                None
            else
                Some fileName

    let private isCompleteRulesetReference (value: string) =
        value.Trim().Replace('\\', '/').EndsWith(".xml", StringComparison.OrdinalIgnoreCase)

    let private elementValue (element: XElement) name =
        element.Attribute(XName.Get name)
        |> Option.ofObj
        |> Option.map (fun attribute -> attribute.Value)
        |> Option.orElseWith (fun () ->
            element.Element(XName.Get name)
            |> Option.ofObj
            |> Option.map (fun child -> child.Value))

    let private propertiesFromElement (element: XElement) =
        let properties =
            System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        for propertyElement in element.Descendants(XName.Get "property") do
            match elementValue propertyElement "name", elementValue propertyElement "value" with
            | Some name, Some value -> properties[name] <- value
            | _ -> ()

        properties |> Seq.map (fun item -> item.Key, item.Value) |> Map.ofSeq

    let private excludedNames (element: XElement) =
        element.Descendants(XName.Get "exclude")
        |> Seq.choose (fun item -> elementValue item "name")
        |> Seq.map (fun name -> name.ToLowerInvariant())
        |> Set.ofSeq

    let private directExcludedNames (element: XElement) =
        element.Elements(XName.Get "exclude")
        |> Seq.choose (fun item -> elementValue item "name")
        |> Seq.map (fun name -> name.ToLowerInvariant())
        |> Set.ofSeq

    let private definition (name: string) =
        Rules.byName |> Map.tryFind (name.ToLowerInvariant())

    let private selection (rulesetName: string) (rule: RuleImplementation) priority properties =
        { Name = rule.Name
          RulesetName = rulesetName
          Priority = defaultArg priority rule.DefaultPriority
          Properties = Map.fold (fun current key value -> Map.add key value current) rule.DefaultProperties properties }

    let private expandBuiltIn
        (rulesetName: string)
        (ruleNames: string list)
        (overrides: Map<string, int option * Map<string, string>>)
        (exclusions: Set<string>)
        =
        ruleNames
        |> List.choose (fun ruleName ->
            match definition ruleName with
            | None -> None
            | Some rule when exclusions.Contains(rule.Name.ToLowerInvariant()) -> None
            | Some rule ->
                let priority, properties =
                    match Map.tryFind rule.Name overrides with
                    | Some values -> values
                    | None -> None, Map.empty

                Some(selection rulesetName rule priority properties))

    let private parseCustom (path: string) : Result<Loaded, string list> =
        try
            let document = XDocument.Load(path, LoadOptions.PreserveWhitespace)
            let root = document.Root

            if isNull root then
                Error [ sprintf "Custom ruleset '%s' is empty." path ]
            else
                let rulesetName =
                    defaultArg (elementValue root "name") (Path.GetFileNameWithoutExtension(path))

                let mutable warnings = []
                let mutable errors = []
                let mutable selections = []
                let globalExclusions = directExcludedNames root

                let addReference referenceElement =
                    match elementValue referenceElement "ref", elementValue referenceElement "name" with
                    | Some reference, _
                    | None, Some reference ->
                        match builtInRuleset reference with
                        | Some(_, ruleNames) ->
                            selections <- (expandBuiltIn rulesetName ruleNames Map.empty globalExclusions) @ selections
                        | None ->
                            match ruleNameFromText reference, rulesetNameFromText reference with
                            | Some _, Some referencedRuleset when isCompleteRulesetReference reference ->
                                match builtInRuleset referencedRuleset with
                                | Some(_, ruleNames) ->
                                    selections <-
                                        (expandBuiltIn rulesetName ruleNames Map.empty globalExclusions) @ selections
                                | None ->
                                    warnings <-
                                        sprintf "Unknown referenced ruleset '%s' in '%s'." reference path :: warnings
                            | Some ruleName, Some referencedRuleset ->
                                match builtInRuleset referencedRuleset, definition ruleName with
                                | Some _, Some rule ->
                                    let priority =
                                        elementValue referenceElement "priority"
                                        |> Option.bind (fun value ->
                                            match Int32.TryParse(value: string) with
                                            | true, parsed -> Some parsed
                                            | _ -> None)

                                    selections <-
                                        selection rulesetName rule priority (propertiesFromElement referenceElement)
                                        :: selections
                                | None, _ ->
                                    warnings <-
                                        sprintf "Unknown referenced ruleset '%s' in '%s'." referencedRuleset path
                                        :: warnings
                                | Some _, None ->
                                    warnings <-
                                        sprintf "Unknown referenced rule '%s' in '%s'." ruleName path :: warnings
                            | Some ruleName, _ ->
                                match definition ruleName with
                                | Some rule ->
                                    let priority =
                                        elementValue referenceElement "priority"
                                        |> Option.bind (fun value ->
                                            match Int32.TryParse(value: string) with
                                            | true, parsed -> Some parsed
                                            | _ -> None)

                                    if not (globalExclusions.Contains(rule.Name.ToLowerInvariant())) then
                                        selections <-
                                            selection rulesetName rule priority (propertiesFromElement referenceElement)
                                            :: selections
                                | None ->
                                    warnings <-
                                        sprintf "Unknown referenced rule '%s' in '%s'." ruleName path :: warnings
                            | _ ->
                                warnings <-
                                    sprintf "Could not understand ruleset reference '%s' in '%s'." reference path
                                    :: warnings
                    | _ -> ()

                for referenceElement in root.Descendants(XName.Get "ruleset") do
                    if referenceElement <> root then
                        addReference referenceElement

                for ruleElement in root.Descendants(XName.Get "rule") do
                    let excluded = excludedNames ruleElement

                    match elementValue ruleElement "ref", elementValue ruleElement "name" with
                    | Some reference, _ ->
                        match ruleNameFromText reference, rulesetNameFromText reference with
                        | Some _, Some referencedRuleset when isCompleteRulesetReference reference ->
                            match builtInRuleset referencedRuleset with
                            | Some(_, ruleNames) ->
                                let overrides =
                                    ruleNames
                                    |> List.choose (fun ruleName ->
                                        definition ruleName
                                        |> Option.map (fun rule ->
                                            rule.Name,
                                            (elementValue ruleElement "priority"
                                             |> Option.bind (fun value ->
                                                 match Int32.TryParse(value: string) with
                                                 | true, parsed -> Some parsed
                                                 | _ -> None),
                                             propertiesFromElement ruleElement)))
                                    |> Map.ofList

                                selections <-
                                    (expandBuiltIn rulesetName ruleNames overrides (Set.union globalExclusions excluded))
                                    @ selections
                            | None ->
                                warnings <-
                                    sprintf "Unknown referenced ruleset '%s' in '%s'." reference path :: warnings
                        | Some ruleName, Some referencedRuleset ->
                            match builtInRuleset referencedRuleset, definition ruleName with
                            | Some _, Some rule ->
                                let priority =
                                    elementValue ruleElement "priority"
                                    |> Option.bind (fun value ->
                                        match Int32.TryParse(value: string) with
                                        | true, parsed -> Some parsed
                                        | _ -> None)

                                if
                                    not (globalExclusions.Contains(rule.Name.ToLowerInvariant()))
                                    && not (excluded.Contains(rule.Name.ToLowerInvariant()))
                                then
                                    selections <-
                                        selection rulesetName rule priority (propertiesFromElement ruleElement)
                                        :: selections
                            | None, _ ->
                                warnings <-
                                    sprintf "Unknown referenced ruleset '%s' in '%s'." referencedRuleset path
                                    :: warnings
                            | Some _, None ->
                                warnings <- sprintf "Unknown referenced rule '%s' in '%s'." ruleName path :: warnings
                        | Some ruleName, _ ->
                            match definition ruleName with
                            | Some rule ->
                                let priority =
                                    elementValue ruleElement "priority"
                                    |> Option.bind (fun value ->
                                        match Int32.TryParse(value: string) with
                                        | true, parsed -> Some parsed
                                        | _ -> None)

                                if
                                    not (globalExclusions.Contains(rule.Name.ToLowerInvariant()))
                                    && not (excluded.Contains(rule.Name.ToLowerInvariant()))
                                then
                                    selections <-
                                        selection rulesetName rule priority (propertiesFromElement ruleElement)
                                        :: selections
                            | None ->
                                warnings <- sprintf "Unknown referenced rule '%s' in '%s'." ruleName path :: warnings
                        | _ ->
                            warnings <-
                                sprintf "Could not understand rule reference '%s' in '%s'." reference path
                                :: warnings
                    | None, Some ruleName ->
                        match definition ruleName with
                        | Some rule when
                            not (globalExclusions.Contains(rule.Name.ToLowerInvariant()))
                            && not (excluded.Contains(rule.Name.ToLowerInvariant()))
                            ->
                            let priority =
                                elementValue ruleElement "priority"
                                |> Option.bind (fun value ->
                                    match Int32.TryParse(value: string) with
                                    | true, parsed -> Some parsed
                                    | _ -> None)

                            selections <-
                                selection rulesetName rule priority (propertiesFromElement ruleElement)
                                :: selections
                        | Some _ -> ()
                        | None -> errors <- sprintf "Unknown rule '%s' in '%s'." ruleName path :: errors
                    | _ -> ()

                if List.isEmpty errors then
                    Ok(
                        { Selections = selections |> List.rev
                          Warnings = warnings |> List.rev }
                    )
                else
                    Error(errors |> List.rev)
        with ex ->
            Error [ sprintf "Could not read custom ruleset '%s': %s" path ex.Message ]

    let private loadOne (name: string) : Result<Loaded, string list> =
        if File.Exists(name) then
            parseCustom (Path.GetFullPath(name))
        else
            match builtInRuleset name with
            | Some(rulesetName, ruleNames) ->
                let overrides =
                    if rulesetName = "fsharp" then
                        Map.ofList [ "LongVariable", (Some 3, Map.ofList [ "maximum", "35" ]) ]
                    else
                        Map.empty

                Ok
                    { Selections = expandBuiltIn rulesetName ruleNames overrides Set.empty
                      Warnings = [] }
            | None -> Error [ sprintf "Unknown ruleset '%s'." name ]

    let private deduplicate (selections: RuleSelection list) =
        let seen =
            System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)

        selections
        |> List.rev
        |> List.filter (fun item -> seen.Add(item.Name))
        |> List.rev

    let load (names: string list) : Result<Loaded, string list> =
        let mutable errors = []
        let mutable selections: RuleSelection list = []
        let mutable warnings = []

        for name in names do
            match loadOne name with
            | Ok loaded ->
                selections <- selections @ loaded.Selections
                warnings <- warnings @ loaded.Warnings
            | Error loadErrors -> errors <- errors @ loadErrors

        if List.isEmpty errors then
            Ok
                { Selections = deduplicate selections
                  Warnings = warnings }
        else
            Error errors

    let applyFilters (options: AnalysisOptions) loaded =
        let requested = options.Only @ options.Enable

        let requestedSet =
            requested |> List.map (fun name -> name.ToLowerInvariant()) |> Set.ofList

        let disabledSet =
            options.Disable |> List.map (fun name -> name.ToLowerInvariant()) |> Set.ofList

        let missingRequested =
            requestedSet
            |> Set.filter (fun requestedName ->
                loaded.Selections
                |> List.exists (fun selection -> selection.Name.ToLowerInvariant() = requestedName)
                |> not)

        let missingDisabled =
            disabledSet
            |> Set.filter (fun disabledName ->
                loaded.Selections
                |> List.exists (fun selection -> selection.Name.ToLowerInvariant() = disabledName)
                |> not)

        if not (Set.isEmpty missingRequested) then
            Error
                [ sprintf
                      "Requested rule '%s' is not present in the loaded rulesets."
                      (missingRequested |> Set.minElement) ]
        elif not (Set.isEmpty missingDisabled) then
            Error
                [ sprintf
                      "Disabled rule '%s' is not present in the loaded rulesets."
                      (missingDisabled |> Set.minElement) ]
        else
            let selected =
                loaded.Selections
                |> List.filter (fun selection ->
                    (Set.isEmpty requestedSet
                     || requestedSet.Contains(selection.Name.ToLowerInvariant()))
                    && not (disabledSet.Contains(selection.Name.ToLowerInvariant()))
                    && (options.MinimumPriority
                        |> Option.forall (fun minimum -> selection.Priority <= minimum))
                    && (options.MaximumPriority
                        |> Option.forall (fun maximum -> selection.Priority >= maximum)))

            Ok { loaded with Selections = selected }
