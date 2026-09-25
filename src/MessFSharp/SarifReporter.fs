namespace MessFSharp

open System
open System.Text.Json
open Domain
open ReportSupport

module internal SarifReporter =
    let format (_color: bool) (report: Report) =
        let ruleIds =
            report.Violations
            |> List.map (fun violation -> violation.RuleName)
            |> List.distinct

        let rules =
            ruleIds
            |> List.map (fun ruleId ->
                let sample =
                    report.Violations |> List.find (fun violation -> violation.RuleName = ruleId)

                {| id = ruleId
                   name = ruleId
                   shortDescription = {| text = sprintf "messfsharp %s" ruleId |}
                   helpUri =
                    sample.HelpUri
                    |> Option.defaultValue (
                        sprintf "https://github.com/quality-gates/messfsharp#%s" (ruleId.ToLowerInvariant())
                    ) |})
            |> List.toArray

        let results =
            report.Violations
            |> List.map (fun violation ->
                {| ruleId = violation.RuleName
                   level =
                    if violation.Priority <= 2 then "error"
                    else if violation.Priority = 3 then "warning"
                    else "note"
                   message = {| text = violation.Description |}
                   properties =
                    {| ruleset = violation.RulesetName
                       priority = violation.Priority
                       context = contextValue violation.Context
                       helpUri = violation.HelpUri |}
                   locations =
                    [| {| physicalLocation =
                           {| artifactLocation = {| uri = violation.Location.File |}
                              region =
                               {| startLine = violation.Location.StartLine
                                  startColumn = violation.Location.StartColumn
                                  endLine = violation.Location.EndLine
                                  endColumn = violation.Location.EndColumn |} |} |} |] |})
            |> List.toArray

        let notifications =
            report.Errors
            |> List.map (fun error ->
                let location = error.Location

                {| level = "error"
                   message = {| text = error.Message |}
                   locations =
                    [| {| physicalLocation =
                           {| artifactLocation = {| uri = error.File |> Option.defaultValue "messfsharp" |}
                              region =
                               {| startLine =
                                   location |> Option.map (fun item -> item.StartLine) |> Option.defaultValue 1
                                  startColumn =
                                   location |> Option.map (fun item -> item.StartColumn) |> Option.defaultValue 1
                                  endLine = location |> Option.map (fun item -> item.EndLine) |> Option.defaultValue 1
                                  endColumn =
                                   location |> Option.map (fun item -> item.EndColumn) |> Option.defaultValue 1 |} |} |} |] |})
            |> List.toArray

        let driver =
            {| name = report.ToolName
               version = report.Version
               rules = rules |}

        let tool = {| driver = driver |}

        let invocation =
            {| executionSuccessful = List.isEmpty report.Errors
               toolExecutionNotifications = notifications |}

        let run =
            {| tool = tool
               results = results
               invocations = [| invocation |] |}

        let document =
            {| ``$schema`` = "https://json.schemastore.org/sarif-2.1.0.json"
               version = "2.1.0"
               runs = [| run |] |}

        JsonSerializer.Serialize(document, jsonOptions ()) + Environment.NewLine
