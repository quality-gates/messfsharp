namespace MessFSharp

open System
open System.IO
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
module Engine =
    type Result =
        { Report: Report
          Warnings: string list
          ExitCode: int }

    let private toolName = "messfsharp"

    let private sourceFile path =
        try
            let text = File.ReadAllText(path)

            Ok
                { FullPath = path
                  Kind = SourceKind.ofPath path
                  Text = text
                  Lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n') }
        with ex ->
            Error
                { File = Some path
                  Location = None
                  Message = sprintf "Could not read source file: %s" ex.Message }

    let private isSuppressed (file: AnalyzedFile) (violation: Violation) =
        file.Declarations
        |> List.exists (fun declaration ->
            declaration.Location.StartLine <= violation.Location.StartLine
            && declaration.ScopeEndLine >= violation.Location.StartLine
            && declaration.SuppressedRules.Contains(violation.RuleName))

    let private runRules (file: AnalyzedFile) (selections: RuleSelection list) =
        selections
        |> List.collect (fun selection ->
            match Rules.byName |> Map.tryFind (selection.Name.ToLowerInvariant()) with
            | Some implementation -> implementation.Check file selection
            | None -> [])

    let private violationKey (violation: Violation) =
        violation.Location.File,
        violation.Location.StartLine,
        violation.Location.StartColumn,
        violation.RuleName,
        violation.Description

    let private distinctViolations violations = List.distinctBy violationKey violations

    let private applySuppression strict file violations =
        if strict then
            violations
        else
            violations |> List.filter (isSuppressed file >> not)

    let private sortViolations violations =
        violations
        |> List.sortWith (fun left right ->
            let fileComparison =
                StringComparer.Ordinal.Compare(left.Location.File, right.Location.File)

            if fileComparison <> 0 then
                fileComparison
            else
                let startLineComparison = compare left.Location.StartLine right.Location.StartLine

                if startLineComparison <> 0 then
                    startLineComparison
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

    let private sortErrors errors =
        errors
        |> List.sortWith (fun left right ->
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

    let private calculateExitCode options report =
        let hasErrors = not (List.isEmpty report.Errors)
        let hasViolations = not (List.isEmpty report.Violations)

        if hasErrors && not options.IgnoreErrorsOnExit then
            1
        elif hasViolations && not options.IgnoreViolationsOnExit then
            2
        else
            0

    let private invalidReport version errors =
        { ToolName = toolName
          Version = version
          Violations = []
          Errors = errors }

    let run version options =
        let reportWithRulesetErrors rulesetErrors warnings =
            let report =
                invalidReport
                    version
                    (rulesetErrors
                     |> List.map (fun message ->
                         { File = None
                           Location = None
                           Message = message }))

            { Report = report
              Warnings = warnings
              ExitCode = calculateExitCode options report }

        match Rulesets.load options.Rulesets with
        | Error errors -> reportWithRulesetErrors errors []
        | Ok loaded ->
            match Rulesets.applyFilters options loaded with
            | Error errors -> reportWithRulesetErrors errors loaded.Warnings
            | Ok filtered ->
                let discoveredFiles, discoveryErrors = Discovery.discover options
                let processingErrors = ResizeArray<ProcessingError>()

                for error in discoveryErrors do
                    processingErrors.Add(error)

                let analyzedFiles = ResizeArray<AnalyzedFile>()

                for path in discoveredFiles do
                    match sourceFile path with
                    | Error error -> processingErrors.Add(error)
                    | Ok source ->
                        match Parsing.parse source with
                        | Error errors ->
                            for error in errors do
                                processingErrors.Add(error)
                        | Ok() -> analyzedFiles.Add(Model.analyze source)

                let violations = ResizeArray<Violation>()

                for file in analyzedFiles do
                    for violation in
                        runRules file filtered.Selections
                        |> distinctViolations
                        |> applySuppression options.Strict file do
                        violations.Add(violation)

                let report =
                    { ToolName = toolName
                      Version = version
                      Violations = violations |> Seq.toList |> distinctViolations |> sortViolations
                      Errors = processingErrors |> Seq.toList |> sortErrors }

                { Report = report
                  Warnings = filtered.Warnings
                  ExitCode = calculateExitCode options report }

    let writeReport options report =
        try
            let content = Reports.render options.Format options.Color report

            match options.ReportFile with
            | None ->
                Console.Out.Write(content)
                Ok()
            | Some path ->
                let fullPath = Path.GetFullPath(path)
                File.WriteAllText(fullPath, content)
                Ok()
        with ex ->
            Error(sprintf "Could not render or write report: %s" ex.Message)
