namespace MessFSharp

open System
open System.Text
open Domain

module internal HtmlReporter =
    let format (_color: bool) (report: Report) =
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

        for violation in report.Violations do
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

            for error in report.Errors do
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
