namespace MessFSharp

open System
open System.Text
open Domain

module internal TextReporter =
    let private visiblePath (path: string) =
        if String.IsNullOrWhiteSpace path then "<unknown>" else path

    let format color report =
        let builder = StringBuilder()

        for violation in report.Violations do
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

        for error in report.Errors do
            let location =
                error.Location
                |> Option.map (fun item -> sprintf ":%d" item.StartLine)
                |> Option.defaultValue ""

            builder.AppendLine(
                sprintf "%s%s: error: %s" (error.File |> Option.defaultValue "messfsharp") location error.Message
            )
            |> ignore

        builder.ToString()
