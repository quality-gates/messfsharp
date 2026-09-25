namespace MessFSharp

open Domain

module Reporter =
    let format (format: ReportFormat) (color: bool) (report: Report) : string =
        let adapter: bool -> Report -> string =
            match format with
            | Text -> TextReporter.format
            | Xml -> XmlReporter.format
            | Json -> JsonReporter.format
            | Html -> HtmlReporter.format
            | Ansi -> fun _ report -> TextReporter.format true report
            | GitHub -> GitHubReporter.format
            | GitLab -> GitLabReporter.format
            | Checkstyle -> CheckstyleReporter.format
            | Sarif -> SarifReporter.format

        adapter color report
