namespace MessFSharp

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Xml.Linq
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessivePublicCount")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyPublicMethods")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
module Reports =
    let private escapeAnnotation (value: string) =
        value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A").Replace(":", "%3A").Replace(",", "%2C")

    let private visiblePath (path: string) =
        if String.IsNullOrWhiteSpace path then "<unknown>" else path

    let private sortedViolations report =
        report.Violations
        |> List.sortWith (fun left right ->
            let fileComparison =
                StringComparer.Ordinal.Compare(left.Location.File, right.Location.File)

            if fileComparison <> 0 then
                fileComparison
            else
                let lineComparison = compare left.Location.StartLine right.Location.StartLine

                if lineComparison <> 0 then
                    lineComparison
                else
                    let endLineComparison = compare left.Location.EndLine right.Location.EndLine

                    if endLineComparison <> 0 then
                        endLineComparison
                    else
                        let ruleComparison = StringComparer.Ordinal.Compare(left.RuleName, right.RuleName)

                        if ruleComparison <> 0 then
                            ruleComparison
                        else
                            StringComparer.Ordinal.Compare(left.Description, right.Description))

    let private sortedErrors (report: Report) =
        report.Errors
        |> List.sortWith (fun (left: ProcessingError) (right: ProcessingError) ->
            let leftFile = left.File |> Option.defaultValue ""
            let rightFile = right.File |> Option.defaultValue ""
            let fileComparison = StringComparer.Ordinal.Compare(leftFile, rightFile)

            if fileComparison <> 0 then
                fileComparison
            else
                let leftLine =
                    left.Location
                    |> Option.map (fun location -> location.StartLine)
                    |> Option.defaultValue 0

                let rightLine =
                    right.Location
                    |> Option.map (fun location -> location.StartLine)
                    |> Option.defaultValue 0

                let lineComparison = compare leftLine rightLine

                if lineComparison <> 0 then
                    lineComparison
                else
                    StringComparer.Ordinal.Compare(left.Message, right.Message))

    let private locationValue (location: SourceLocation) =
        {| file = location.File
           startLine = location.StartLine
           startColumn = location.StartColumn
           endLine = location.EndLine
           endColumn = location.EndColumn |}

    let private contextValue context =
        {| ``namespace`` = context.Namespace
           ``module`` = context.Module
           ``type`` = context.Type
           ``member`` = context.Member |}

    let private jsonValue report =
        let violations =
            sortedViolations report
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
            sortedErrors report
            |> List.map (fun error ->
                {| file = error.File
                   location = error.Location |> Option.map locationValue
                   message = error.Message |})
            |> List.toArray

        {| tool = report.ToolName
           version = report.Version
           violations = violations
           errors = errors |}

    let private jsonOptions () =
        let options = JsonSerializerOptions(WriteIndented = true)
        options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        options.DefaultIgnoreCondition <- JsonIgnoreCondition.Never
        options

    let private renderText color report =
        let builder = StringBuilder()

        for violation in sortedViolations report do
            let prefix =
                sprintf
                    "%s:%d:%s: "
                    (visiblePath violation.Location.File)
                    violation.Location.StartLine
                    violation.RuleName

            let line = prefix + violation.Description

            if color then
                let colorCode =
                    if violation.Priority <= 2 then "\u001b[31m"
                    else if violation.Priority = 3 then "\u001b[33m"
                    else "\u001b[36m"

                builder.Append(colorCode).Append(line).Append("\u001b[0m").AppendLine()
                |> ignore
            else
                builder.AppendLine(line) |> ignore

        for error in sortedErrors report do
            let location =
                error.Location
                |> Option.map (fun item -> sprintf ":%d" item.StartLine)
                |> Option.defaultValue ""

            builder.AppendLine(
                sprintf "%s%s: error: %s" (error.File |> Option.defaultValue "messfsharp") location error.Message
            )
            |> ignore

        builder.ToString()

    let private renderXml report =
        let root = XElement(XName.Get "report")
        root.SetAttributeValue(XName.Get "tool", report.ToolName)
        root.SetAttributeValue(XName.Get "version", report.Version)
        let violations = XElement(XName.Get "violations")

        for violation in sortedViolations report do
            let element = XElement(XName.Get "violation")
            element.SetAttributeValue(XName.Get "file", violation.Location.File)
            element.SetAttributeValue(XName.Get "startLine", violation.Location.StartLine)
            element.SetAttributeValue(XName.Get "startColumn", violation.Location.StartColumn)
            element.SetAttributeValue(XName.Get "endLine", violation.Location.EndLine)
            element.SetAttributeValue(XName.Get "endColumn", violation.Location.EndColumn)
            element.SetAttributeValue(XName.Get "rule", violation.RuleName)
            element.SetAttributeValue(XName.Get "ruleset", violation.RulesetName)
            element.SetAttributeValue(XName.Get "priority", violation.Priority)
            element.SetAttributeValue(XName.Get "helpUri", violation.HelpUri |> Option.defaultValue "")
            element.Add(XElement(XName.Get "description", violation.Description))
            let context = XElement(XName.Get "context")
            context.SetAttributeValue(XName.Get "namespace", violation.Context.Namespace |> Option.defaultValue "")
            context.SetAttributeValue(XName.Get "module", violation.Context.Module |> Option.defaultValue "")
            context.SetAttributeValue(XName.Get "type", violation.Context.Type |> Option.defaultValue "")
            context.SetAttributeValue(XName.Get "member", violation.Context.Member |> Option.defaultValue "")
            element.Add(context)
            violations.Add(element)

        root.Add(violations)
        let errors = XElement(XName.Get "errors")

        for error in sortedErrors report do
            let element = XElement(XName.Get "error", error.Message)
            element.SetAttributeValue(XName.Get "file", error.File |> Option.defaultValue "")

            match error.Location with
            | Some location ->
                element.SetAttributeValue(XName.Get "startLine", location.StartLine)
                element.SetAttributeValue(XName.Get "startColumn", location.StartColumn)
            | None -> ()

            errors.Add(element)

        root.Add(errors)
        XDocument(root).ToString(SaveOptions.DisableFormatting) + Environment.NewLine

    let private renderHtml report =
        let encode = System.Net.WebUtility.HtmlEncode
        let builder = StringBuilder()

        let contextText context =
            [ context.Namespace; context.Module; context.Type; context.Member ]
            |> List.choose id
            |> String.concat "."

        builder.AppendLine("<!doctype html>") |> ignore

        builder.AppendLine(
            "<html><head><meta charset=\"utf-8\"><title>messfsharp report</title><style>body{font-family:system-ui,sans-serif}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ddd;padding:.4rem;text-align:left}.p1,.p2{color:#b00020}.p3{color:#8a5700}</style></head><body>"
        )
        |> ignore

        builder.AppendLine(
            sprintf
                "<h1>messfsharp %s</h1><table><thead><tr><th>File</th><th>Start</th><th>End</th><th>Rule</th><th>Ruleset</th><th>Priority</th><th>Context</th><th>Description</th><th>Help</th></tr></thead><tbody>"
                (encode report.Version)
        )
        |> ignore

        for violation in sortedViolations report do
            let help =
                violation.HelpUri
                |> Option.map (fun uri -> sprintf "<a href=\"%s\">%s</a>" (encode uri) (encode uri))
                |> Option.defaultValue ""

            builder.AppendLine(
                sprintf
                    "<tr class=\"p%d\"><td>%s</td><td>%d:%d</td><td>%d:%d</td><td>%s</td><td>%s</td><td>%d</td><td>%s</td><td>%s</td><td>%s</td></tr>"
                    violation.Priority
                    (encode violation.Location.File)
                    violation.Location.StartLine
                    violation.Location.StartColumn
                    violation.Location.EndLine
                    violation.Location.EndColumn
                    (encode violation.RuleName)
                    (encode violation.RulesetName)
                    violation.Priority
                    (encode (contextText violation.Context))
                    (encode violation.Description)
                    help
            )
            |> ignore

        builder.AppendLine("</tbody></table>") |> ignore

        if not (List.isEmpty report.Errors) then
            builder.AppendLine("<h2>Processing errors</h2><ul>") |> ignore

            for error in sortedErrors report do
                let location =
                    error.Location
                    |> Option.map (fun item -> sprintf ":%d:%d" item.StartLine item.StartColumn)
                    |> Option.defaultValue ""

                builder.AppendLine(
                    sprintf
                        "<li>%s%s: %s</li>"
                        (encode (error.File |> Option.defaultValue "messfsharp"))
                        location
                        (encode error.Message)
                )
                |> ignore

            builder.AppendLine("</ul>") |> ignore

        builder.AppendLine("</body></html>") |> ignore
        builder.ToString()

    let private renderGitHub report =
        let builder = StringBuilder()

        for violation in sortedViolations report do
            let level = if violation.Priority <= 2 then "error" else "warning"

            builder.AppendLine(
                sprintf
                    "::%s file=%s,line=%d,col=%d,endLine=%d,endColumn=%d,title=%s::%s"
                    level
                    (escapeAnnotation violation.Location.File)
                    violation.Location.StartLine
                    violation.Location.StartColumn
                    violation.Location.EndLine
                    violation.Location.EndColumn
                    (escapeAnnotation violation.RuleName)
                    (escapeAnnotation violation.Description)
            )
            |> ignore

        for error in sortedErrors report do
            match error.File, error.Location with
            | Some file, Some location ->
                builder.AppendLine(
                    sprintf
                        "::error file=%s,line=%d,col=%d,title=messfsharp::%s"
                        (escapeAnnotation file)
                        location.StartLine
                        location.StartColumn
                        (escapeAnnotation error.Message)
                )
                |> ignore
            | _ ->
                builder.AppendLine(sprintf "::error title=messfsharp::%s" (escapeAnnotation error.Message))
                |> ignore

        builder.ToString()

    let private severity priority =
        if priority <= 1 then "blocker"
        else if priority = 2 then "critical"
        else if priority = 3 then "major"
        else if priority = 4 then "minor"
        else "info"

    let private checkstyleSeverity priority =
        if priority <= 2 then "error"
        else if priority <= 4 then "warning"
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

    let private renderGitLab report =
        let emptyContext =
            {| ``namespace`` = (None: string option)
               ``module`` = (None: string option)
               ``type`` = (None: string option)
               ``member`` = (None: string option) |}

        let findings =
            sortedViolations report
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

        for error in sortedErrors report do
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

    let private renderCheckstyle report =
        let root = XElement(XName.Get "checkstyle")
        root.SetAttributeValue(XName.Get "version", "10.3")
        root.SetAttributeValue(XName.Get "tool", report.ToolName)
        root.SetAttributeValue(XName.Get "toolVersion", report.Version)

        let grouped =
            sortedViolations report
            |> List.groupBy (fun violation -> violation.Location.File)

        for file, violations in grouped do
            let fileElement = XElement(XName.Get "file")
            fileElement.SetAttributeValue(XName.Get "name", file)

            for violation in violations do
                let error = XElement(XName.Get "error")
                error.SetAttributeValue(XName.Get "line", violation.Location.StartLine)
                error.SetAttributeValue(XName.Get "column", violation.Location.StartColumn)
                error.SetAttributeValue(XName.Get "endLine", violation.Location.EndLine)
                error.SetAttributeValue(XName.Get "endColumn", violation.Location.EndColumn)
                error.SetAttributeValue(XName.Get "severity", checkstyleSeverity violation.Priority)
                error.SetAttributeValue(XName.Get "message", violation.Description)
                error.SetAttributeValue(XName.Get "source", sprintf "messfsharp.%s" violation.RuleName)
                error.SetAttributeValue(XName.Get "ruleset", violation.RulesetName)
                error.SetAttributeValue(XName.Get "priority", violation.Priority)
                error.SetAttributeValue(XName.Get "namespace", violation.Context.Namespace |> Option.defaultValue "")
                error.SetAttributeValue(XName.Get "module", violation.Context.Module |> Option.defaultValue "")
                error.SetAttributeValue(XName.Get "type", violation.Context.Type |> Option.defaultValue "")
                error.SetAttributeValue(XName.Get "member", violation.Context.Member |> Option.defaultValue "")
                error.SetAttributeValue(XName.Get "helpUri", violation.HelpUri |> Option.defaultValue "")
                fileElement.Add(error)

            root.Add(fileElement)

        for error in sortedErrors report do
            let fileElement = XElement(XName.Get "file")
            fileElement.SetAttributeValue(XName.Get "name", error.File |> Option.defaultValue "messfsharp")
            let errorElement = XElement(XName.Get "error")

            errorElement.SetAttributeValue(
                XName.Get "line",
                error.Location
                |> Option.map (fun item -> item.StartLine)
                |> Option.defaultValue 1
            )

            errorElement.SetAttributeValue(XName.Get "severity", "error")
            errorElement.SetAttributeValue(XName.Get "message", error.Message)
            errorElement.SetAttributeValue(XName.Get "source", "messfsharp.processing")

            match error.Location with
            | Some location ->
                errorElement.SetAttributeValue(XName.Get "column", location.StartColumn)
                errorElement.SetAttributeValue(XName.Get "endLine", location.EndLine)
                errorElement.SetAttributeValue(XName.Get "endColumn", location.EndColumn)
            | None -> ()

            fileElement.Add(errorElement)
            root.Add(fileElement)

        XDocument(root).ToString(SaveOptions.DisableFormatting) + Environment.NewLine

    let private renderSarif report =
        let ruleIds =
            sortedViolations report
            |> List.map (fun violation -> violation.RuleName)
            |> List.distinct

        let rules =
            ruleIds
            |> List.map (fun ruleId ->
                let sample =
                    sortedViolations report
                    |> List.find (fun violation -> violation.RuleName = ruleId)

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
            sortedViolations report
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
            sortedErrors report
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

    let render format color report =
        match format with
        | Text -> renderText color report
        | Xml -> renderXml report
        | Json -> JsonSerializer.Serialize(jsonValue report, jsonOptions ()) + Environment.NewLine
        | Html -> renderHtml report
        | Ansi -> renderText true report
        | GitHub -> renderGitHub report
        | GitLab -> renderGitLab report
        | Checkstyle -> renderCheckstyle report
        | Sarif -> renderSarif report
