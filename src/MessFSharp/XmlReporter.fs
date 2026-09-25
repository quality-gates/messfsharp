namespace MessFSharp

open System
open System.Xml.Linq
open Domain

module internal XmlReporter =
    let format (_color: bool) (report: Report) =
        let root = XElement(XName.Get "report")
        root.SetAttributeValue(XName.Get "tool", report.ToolName)
        root.SetAttributeValue(XName.Get "version", report.Version)
        let violations = XElement(XName.Get "violations")

        for violation in report.Violations do
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

        for error in report.Errors do
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
