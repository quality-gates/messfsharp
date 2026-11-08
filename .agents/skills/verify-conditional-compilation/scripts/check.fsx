#r "../../../../src/MessFSharp/bin/Release/net10.0/FSharp.Compiler.Service.dll"
#r "../../../../src/MessFSharp/bin/Release/net10.0/messfsharp.dll"

open System
open System.IO
open MessFSharp
open MessFSharp.Domain

type Symbol =
    | Absent
    | Compiled
    | Editing
    | Interactive

type Condition =
    | Defined of Symbol
    | NotDefined of Symbol

type Branch =
    | Then
    | Else

type Layout =
    | NoElse
    | Flat
    | Nested of Branch * Condition
    | Both of Condition * Condition

type Case =
    | Unconditional of SourceKind
    | Conditional of SourceKind * Condition * Layout

type RenderedCase =
    { Id: string
      Kind: SourceKind
      Text: string
      ExpectedLines: int list }

type Failure = { Case: RenderedCase; Actual: string }

let kinds = [ Implementation; Signature; Script ]

let conditions =
    [ for symbol in [ Absent; Compiled; Editing; Interactive ] do
          Defined symbol
          NotDefined symbol ]

let kindName =
    function
    | Implementation -> "fs"
    | Signature -> "fsi"
    | Script -> "fsx"

let symbolName =
    function
    | Absent -> "ABSENT"
    | Compiled -> "COMPILED"
    | Editing -> "EDITING"
    | Interactive -> "INTERACTIVE"

let conditionText =
    function
    | Defined symbol -> symbolName symbol
    | NotDefined symbol -> "!" + symbolName symbol

let conditionName =
    function
    | Defined symbol -> (symbolName symbol).ToLowerInvariant()
    | NotDefined symbol -> "not-" + (symbolName symbol).ToLowerInvariant()

let isActive kind condition =
    let symbolIsDefined =
        function
        | Absent -> false
        | Compiled -> kind <> Script
        | Editing -> true
        | Interactive -> kind = Script

    match condition with
    | Defined symbol -> symbolIsDefined symbol
    | NotDefined symbol -> not (symbolIsDefined symbol)

let branchName =
    function
    | Then -> "then"
    | Else -> "else"

let caseId =
    function
    | Unconditional kind -> kindName kind + "-unconditional"
    | Conditional(kind, outer, layout) ->
        let prefix = kindName kind + "-" + conditionName outer

        match layout with
        | NoElse -> prefix + "-no-else"
        | Flat -> prefix + "-flat"
        | Nested(branch, inner) -> prefix + "-" + branchName branch + "-" + conditionName inner
        | Both(left, right) -> prefix + "-both-" + conditionName left + "-" + conditionName right

let allCases () =
    seq {
        for kind in kinds do
            yield Unconditional kind

            for outer in conditions do
                yield Conditional(kind, outer, NoElse)
                yield Conditional(kind, outer, Flat)

                for inner in conditions do
                    yield Conditional(kind, outer, Nested(Then, inner))
                    yield Conditional(kind, outer, Nested(Else, inner))

                    for right in conditions do
                        yield Conditional(kind, outer, Both(inner, right))
    }

let render case =
    let kind =
        match case with
        | Unconditional kind
        | Conditional(kind, _, _) -> kind

    let lines = ResizeArray<string>()
    let expected = ResizeArray<int>()

    let addDeclaration active name =
        let declaration =
            match kind with
            | Signature -> sprintf "val %s: int" name
            | Implementation
            | Script -> sprintf "let %s = 1" name

        lines.Add declaration

        if active then
            expected.Add lines.Count

    let addNested active name condition =
        lines.Add("#if " + conditionText condition)
        let nestedActive = isActive kind condition
        addDeclaration (active && nestedActive) ("Bad" + name + "Then")
        lines.Add "#else"
        addDeclaration (active && not nestedActive) ("Bad" + name + "Else")
        lines.Add "#endif"

    lines.Add "module Generated"
    addDeclaration true "BadBefore"

    match case with
    | Unconditional _ -> ()
    | Conditional(_, outer, layout) ->
        let outerActive = isActive kind outer
        lines.Add("#if " + conditionText outer)
        addDeclaration outerActive "BadOuterThen"

        match layout with
        | Nested(Then, inner)
        | Both(inner, _) -> addNested outerActive "Then" inner
        | _ -> ()

        if layout <> NoElse then
            lines.Add "#else"
            addDeclaration (not outerActive) "BadOuterElse"

            match layout with
            | Nested(Else, inner)
            | Both(_, inner) -> addNested (not outerActive) "Else" inner
            | _ -> ()

        lines.Add "#endif"

    addDeclaration true "BadAfter"

    { Id = caseId case
      Kind = kind
      Text = String.concat "\n" lines + "\n"
      ExpectedLines = List.ofSeq expected }

let rule = Rules.camelCaseVariableName

let selection =
    { Name = rule.Name
      RulesetName = "fsharp"
      Priority = rule.DefaultPriority
      Properties = rule.DefaultProperties }

let check case =
    let rendered = render case

    let path =
        Path.Combine(Path.GetTempPath(), "messfsharp-conditional-" + rendered.Id + "." + kindName rendered.Kind)

    let source = SourceFile.ofText path rendered.Kind rendered.Text

    match Engine.analyzeSource false [ selection ] source with
    | Error errors ->
        Some
            { Case = rendered
              Actual = sprintf "analysis errors: %A" errors }
    | Ok violations ->
        let actual =
            violations
            |> List.map (fun violation -> violation.Location.StartLine)
            |> List.sort

        if actual = rendered.ExpectedLines then
            None
        else
            Some
                { Case = rendered
                  Actual = sprintf "finding lines: %A" actual }

let showFailure failure =
    printfn "\nFAIL %s" failure.Case.Id
    printfn "Expected finding lines: %A" failure.Case.ExpectedLines
    printfn "Actual %s" failure.Actual

    failure.Case.Text.Split('\n')
    |> Array.iteri (fun index line -> printfn "%3d | %s" (index + 1) line)

let run args =
    let selected, listFailures =
        match args with
        | [||] -> None, false
        | [| "--case"; id |] -> Some id, false
        | [| "--list-failures" |] -> None, true
        | _ -> failwith "Usage: check.sh [--case CASE_ID | --list-failures]"

    let cases =
        allCases ()
        |> Seq.filter (fun case -> selected |> Option.forall (fun id -> caseId case = id))
        |> Seq.toArray

    if Array.isEmpty cases then
        failwithf "No case matches %A" selected

    let failures = cases |> Array.choose check
    printfn "Checked %d cases. Failed: %d." cases.Length failures.Length

    if listFailures then
        failures
        |> Array.iter (fun failure ->
            let actual = failure.Actual.Replace("\r", "\\r").Replace("\n", "\\n")
            printfn "FAIL-RESULT %s expected=%A actual=%s" failure.Case.Id failure.Case.ExpectedLines actual)

    for kind in kinds do
        failures
        |> Array.tryFind (fun failure -> failure.Case.Kind = kind)
        |> Option.iter showFailure

    if Array.isEmpty failures then 0 else 1

Environment.Exit(run (fsi.CommandLineArgs |> Array.skip 1))
