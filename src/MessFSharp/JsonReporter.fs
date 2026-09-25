namespace MessFSharp

open System
open System.Text.Json
open Domain
open ReportSupport

module internal JsonReporter =
    let private jsonValue report =
        let violations =
            report.Violations
            |> List.map (fun violation ->
                {| file = violation.Location.File
                   startLine = violation.Location.StartLine
                   startColumn = violation.Location.StartColumn
                   endLine = violation.Location.EndLine
                   endColumn = violation.Location.EndColumn
                   rule = violation.RuleName
                   ruleset = violation.RulesetName
                   priority = violation.Priority
                   description = violation.Description
                   context = contextValue violation.Context
                   helpUri = violation.HelpUri |})
            |> List.toArray

        let errors =
            report.Errors
            |> List.map (fun error ->
                {| file = error.File
                   location = error.Location |> Option.map locationValue
                   message = error.Message |})
            |> List.toArray

        {| tool = report.ToolName
           version = report.Version
           violations = violations
           errors = errors |}

    let format (_color: bool) (report: Report) =
        JsonSerializer.Serialize(jsonValue report, jsonOptions ()) + Environment.NewLine
