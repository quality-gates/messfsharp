namespace MessFSharp

open System
open Domain

module FindingAttribution =
    let private context (file: AnalyzedFile) (location: SourceLocation) =
        let enclosing = enclosingDeclarations file.Declarations location.StartLine

        let innermost predicate =
            enclosing
            |> List.filter predicate
            |> List.sortByDescending (fun item -> item.Location.StartLine)
            |> List.tryHead
            |> Option.map (fun item -> item.Name)

        // A primary constructor has the scope of its type. It is the member only for a violation at its own location.
        let isPrimaryConstructor (item: Declaration) =
            item.Kind = Constructor && item.ParentStartLine = Some item.Location.StartLine

        let isMember (item: Declaration) =
            match item.Kind with
            | Member
            | Property -> true
            | Constructor ->
                not (isPrimaryConstructor item)
                || (item.Location.StartLine = location.StartLine
                    && item.Location.StartColumn = location.StartColumn)
            | _ -> false

        { Namespace = innermost (fun item -> item.Kind = Namespace)
          Module = innermost (fun item -> item.Kind = Module)
          Type = innermost (fun item -> item.Kind = Type)
          Member = innermost isMember }

    let private ruleUri (name: string) =
        Some(sprintf "https://github.com/quality-gates/messfsharp#%s" (name.ToLowerInvariant()))

    let private attachFinding
        (file: AnalyzedFile)
        (flowOwners: Lazy<Map<SourceLocation * string, string>>)
        (selection: RuleSelection)
        (finding: RuleFinding)
        =
        let baseContext = context file finding.Location

        let flowOwner =
            if Set.contains (selection.Name.ToLowerInvariant()) Rules.flowRuleNames then
                Map.tryFind (finding.Location, finding.Description) flowOwners.Value
            else
                None

        { Location = finding.Location
          RuleName = selection.Name
          RulesetName = selection.RulesetName
          Priority = selection.Priority
          Description = finding.Description
          Context =
            { baseContext with
                Member = flowOwner |> Option.orElse baseContext.Member }
          HelpUri = ruleUri selection.Name }

    let runRules (file: AnalyzedFile) (selections: RuleSelection list) =
        let flowOwners =
            lazy
                (file.ImplicitFlows.Value
                 |> List.map (fun flow -> (flow.Location, Rules.flowDescription flow), flow.Owner)
                 |> Map.ofList)

        selections
        |> List.collect (fun selection ->
            match Rules.byName |> Map.tryFind (selection.Name.ToLowerInvariant()) with
            | Some implementation ->
                implementation.Check file selection.Properties
                |> List.map (attachFinding file flowOwners selection)
            | None -> [])
