namespace MessFSharp

open System
open System.Text
open Domain

module internal GitHubReporter =
    let private escapeAnnotationProperty (value: string) =
        value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A").Replace(":", "%3A").Replace(",", "%2C")

    let private escapeAnnotationData (value: string) =
        value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A")

    let format (_color: bool) (report: Report) =
        let builder = StringBuilder()

        for violation in report.Violations do
            let level = if violation.Priority <= 2 then "error" else "warning"

            builder.AppendLine(
                sprintf
                    "::%s file=%s,line=%d,col=%d,endLine=%d,endColumn=%d,title=%s::%s"
                    level
                    (escapeAnnotationProperty violation.Location.File)
                    violation.Location.StartLine
                    violation.Location.StartColumn
                    violation.Location.EndLine
                    violation.Location.EndColumn
                    (escapeAnnotationProperty violation.RuleName)
                    (escapeAnnotationData violation.Description)
            )
            |> ignore

        for error in report.Errors do
            match error.File, error.Location with
            | Some file, Some location ->
                builder.AppendLine(
                    sprintf
                        "::error file=%s,line=%d,col=%d,title=messfsharp::%s"
                        (escapeAnnotationProperty file)
                        location.StartLine
                        location.StartColumn
                        (escapeAnnotationData error.Message)
                )
                |> ignore
            | _ ->
                builder.AppendLine(sprintf "::error title=messfsharp::%s" (escapeAnnotationData error.Message))
                |> ignore

        builder.ToString()
