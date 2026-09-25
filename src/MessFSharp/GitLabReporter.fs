namespace MessFSharp

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Domain
open ReportSupport

module internal GitLabReporter =
    let private severity priority =
        if priority <= 1 then "blocker"
        else if priority = 2 then "critical"
        else if priority = 3 then "major"
        else if priority = 4 then "minor"
        else "info"

    let private fingerprint (violation: Violation) =
        use sha = SHA256.Create()

        let value =
            sprintf
                "%s|%d|%s|%s"
                violation.Location.File
                violation.Location.StartLine
                violation.RuleName
                violation.Description

        sha.ComputeHash(Encoding.UTF8.GetBytes(value)) |> Convert.ToHexString

    let format (_color: bool) (report: Report) =
        let emptyContext =
            {| ``namespace`` = (None: string option)
               ``module`` = (None: string option)
               ``type`` = (None: string option)
               ``member`` = (None: string option) |}

        let findings =
            report.Violations
            |> List.map (fun violation ->
                {| description = violation.Description
                   check_name = violation.RuleName
                   fingerprint = fingerprint violation
                   severity = severity violation.Priority
                   tool = report.ToolName
                   version = report.Version
                   ruleset = violation.RulesetName
                   priority = violation.Priority
                   context = contextValue violation.Context
                   help_uri = violation.HelpUri
                   location =
                    {| path = violation.Location.File
                       lines =
                        {| ``begin`` = violation.Location.StartLine
                           ``end`` = violation.Location.EndLine |}
                       positions =
                        {| ``begin`` =
                            {| line = violation.Location.StartLine
                               column = violation.Location.StartColumn |}
                           ``end`` =
                            {| line = violation.Location.EndLine
                               column = violation.Location.EndColumn |} |} |} |})
            |> ResizeArray

        for error in report.Errors do
            findings.Add(
                {| description = error.Message
                   check_name = "messfsharp-processing"
                   fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(error.Message)))
                   severity = "critical"
                   tool = report.ToolName
                   version = report.Version
                   ruleset = ""
                   priority = 1
                   context = emptyContext
                   help_uri = None
                   location =
                    {| path = error.File |> Option.defaultValue "messfsharp"
                       lines =
                        {| ``begin`` =
                            error.Location
                            |> Option.map (fun item -> item.StartLine)
                            |> Option.defaultValue 1
                           ``end`` = error.Location |> Option.map (fun item -> item.EndLine) |> Option.defaultValue 1 |}
                       positions =
                        {| ``begin`` =
                            {| line =
                                error.Location
                                |> Option.map (fun item -> item.StartLine)
                                |> Option.defaultValue 1
                               column =
                                error.Location
                                |> Option.map (fun item -> item.StartColumn)
                                |> Option.defaultValue 1 |}
                           ``end`` =
                            {| line = error.Location |> Option.map (fun item -> item.EndLine) |> Option.defaultValue 1
                               column =
                                error.Location
                                |> Option.map (fun item -> item.EndColumn)
                                |> Option.defaultValue 1 |} |} |} |}
            )

        JsonSerializer.Serialize(findings.ToArray(), jsonOptions ())
        + Environment.NewLine
