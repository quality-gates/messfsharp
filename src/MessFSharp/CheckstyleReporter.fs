namespace MessFSharp

open System
open System.Xml.Linq
open Domain

module internal CheckstyleReporter =
    let private checkstyleSeverity priority =
        if priority <= 2 then "error"
        else if priority <= 4 then "warning"
        else "info"

    let format (_color: bool) (report: Report) =
        let root = XElement(XName.Get "checkstyle")
        root.SetAttributeValue(XName.Get "version", "10.3")
        root.SetAttributeValue(XName.Get "tool", report.ToolName)
        root.SetAttributeValue(XName.Get "toolVersion", report.Version)

        let grouped =
            report.Violations |> List.groupBy (fun violation -> violation.Location.File)

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

        for error in report.Errors do
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
