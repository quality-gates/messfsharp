namespace MessFSharp

open System
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveMethodLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
module Cli =
    let usage =
        """messfsharp - an idiomatic F# mess detector

Usage:
  messfsharp <paths> <format> <ruleset[,ruleset...]> [options]

Paths are comma-separated files or directories. Formats are:
  text, xml, json, html, ansi, github, gitlab, checkstyle, sarif

Rulesets are: fsharp, cleancode, codesize, controversial, design,
  naming, unusedcode, opinionated, or a custom XML ruleset path.

Options:
  --minimumpriority <1..5>       Retain priorities less than or equal to this value.
  --maximumpriority <1..5>       Retain priorities greater than or equal to this value.
  --reportfile <path>            Write the report to a file instead of stdout.
  --suffixes <.fs,...>           Replace the source suffix list.
  --exclude <text,...>           Exclude paths containing these substrings.
  --enable, --only <rule,...>    Select only named loaded rules.
  --disable <rule,...>           Remove named loaded rules.
  --ignore-tests                 Skip common test files and directories.
  --strict                       Include findings with source suppressions.
  --color                        Colorize text output when appropriate.
  --verbose, -v                  Include diagnostic rule-loading warnings.
  --version                      Print the installed version.
  --help, -h, help               Print this help.
  --ignore-errors-on-exit        Do not return 1 for processing errors.
  --ignore-violations-on-exit    Do not return 2 for violations.
"""

    let private optionTakesValue name =
        [ "--minimumpriority"
          "--maximumpriority"
          "--reportfile"
          "--suffixes"
          "--exclude"
          "--enable"
          "--only"
          "--disable" ]
        |> List.contains name

    let private splitValues (value: string) =
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun item -> item.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.toList

    let private parsePriority optionName value =
        match Int32.TryParse(value: string) with
        | true, number when number >= 1 && number <= 5 -> Ok number
        | _ -> Error(sprintf "%s expects a priority between 1 and 5, received '%s'." optionName value)

    let private normalizeSuffixes value =
        let values = splitValues value

        if List.isEmpty values then
            Error "--suffixes requires at least one suffix."
        else
            Ok(
                values
                |> List.map (fun suffix ->
                    if suffix.StartsWith(".", StringComparison.Ordinal) then
                        suffix.ToLowerInvariant()
                    else
                        "." + suffix.ToLowerInvariant())
            )

    let parse (argv: string array) =
        if
            argv |> Array.exists (fun argument -> argument = "--help" || argument = "-h")
            || (argv.Length = 1 && argv[0] = "help")
        then
            Help
        elif argv |> Array.exists (fun argument -> argument = "--version") then
            Version
        elif argv.Length = 0 then
            Invalid "No arguments supplied."
        else
            let rec loop index positional options =
                if index = argv.Length then
                    match positional with
                    | [ paths; format; ruleset ] ->
                        let parsedPaths = splitValues paths
                        let parsedRulesets = splitValues ruleset

                        if List.isEmpty parsedPaths then
                            Invalid "At least one source path is required."
                        elif List.isEmpty parsedRulesets then
                            Invalid "At least one ruleset is required."
                        else
                            match ReportFormat.tryParse format with
                            | None -> Invalid(sprintf "Unknown report format '%s'." format)
                            | Some reportFormat ->
                                Analyze
                                    { options with
                                        Paths = parsedPaths
                                        Format = reportFormat
                                        Rulesets = parsedRulesets }
                    | _ ->
                        Invalid
                            "Analysis requires exactly three positional arguments: <paths> <format> <ruleset[,ruleset...]>."
                else
                    let argument = argv[index]

                    if argument.StartsWith("-", StringComparison.Ordinal) then
                        let optionName, inlineValue =
                            match argument.IndexOf('=') with
                            | equals when equals > 0 ->
                                argument.Substring(0, equals), Some(argument.Substring(equals + 1))
                            | _ -> argument, None

                        let valueForOption () =
                            match inlineValue with
                            | Some value when not (String.IsNullOrWhiteSpace value) -> Ok(value, index + 1)
                            | Some _ -> Error(sprintf "%s requires a value." optionName)
                            | None when index + 1 >= argv.Length -> Error(sprintf "%s requires a value." optionName)
                            | None when argv[index + 1].StartsWith("-", StringComparison.Ordinal) ->
                                Error(sprintf "%s requires a value." optionName)
                            | None when String.IsNullOrWhiteSpace argv[index + 1] ->
                                Error(sprintf "%s requires a value." optionName)
                            | None -> Ok(argv[index + 1], index + 2)

                        let continueWith nextOptions nextIndex = loop nextIndex positional nextOptions

                        match optionName with
                        | "--minimumpriority"
                        | "--maximumpriority" ->
                            match valueForOption () with
                            | Error message -> Invalid message
                            | Ok(value, nextIndex) ->
                                match parsePriority optionName value with
                                | Error message -> Invalid message
                                | Ok priority ->
                                    let nextOptions =
                                        if optionName = "--minimumpriority" then
                                            { options with
                                                MinimumPriority = Some priority }
                                        else
                                            { options with
                                                MaximumPriority = Some priority }

                                    continueWith nextOptions nextIndex
                        | "--reportfile" ->
                            match valueForOption () with
                            | Error message -> Invalid message
                            | Ok(value, nextIndex) -> continueWith { options with ReportFile = Some value } nextIndex
                        | "--suffixes" ->
                            match valueForOption () with
                            | Error message -> Invalid message
                            | Ok(value, nextIndex) ->
                                match normalizeSuffixes value with
                                | Error message -> Invalid message
                                | Ok suffixes -> continueWith { options with Suffixes = suffixes } nextIndex
                        | "--exclude" ->
                            match valueForOption () with
                            | Error message -> Invalid message
                            | Ok(value, nextIndex) ->
                                let excludes = splitValues value

                                if List.isEmpty excludes then
                                    Invalid "--exclude requires at least one path substring."
                                else
                                    continueWith
                                        { options with
                                            Excludes = options.Excludes @ excludes }
                                        nextIndex
                        | "--enable"
                        | "--only" ->
                            match valueForOption () with
                            | Error message -> Invalid message
                            | Ok(value, nextIndex) ->
                                let selected = splitValues value

                                if List.isEmpty selected then
                                    Invalid(sprintf "%s requires at least one rule." optionName)
                                elif optionName = "--enable" then
                                    continueWith
                                        { options with
                                            Enable = options.Enable @ selected }
                                        nextIndex
                                else
                                    continueWith
                                        { options with
                                            Only = options.Only @ selected }
                                        nextIndex
                        | "--disable" ->
                            match valueForOption () with
                            | Error message -> Invalid message
                            | Ok(value, nextIndex) ->
                                let disabled = splitValues value

                                if List.isEmpty disabled then
                                    Invalid "--disable requires at least one rule."
                                else
                                    continueWith
                                        { options with
                                            Disable = options.Disable @ disabled }
                                        nextIndex
                        | "--ignore-tests" -> continueWith { options with IgnoreTests = true } (index + 1)
                        | "--strict" -> continueWith { options with Strict = true } (index + 1)
                        | "--color" -> continueWith { options with Color = true } (index + 1)
                        | "--verbose"
                        | "-v" -> continueWith { options with Verbose = true } (index + 1)
                        | "--ignore-errors-on-exit" ->
                            continueWith
                                { options with
                                    IgnoreErrorsOnExit = true }
                                (index + 1)
                        | "--ignore-violations-on-exit" ->
                            continueWith
                                { options with
                                    IgnoreViolationsOnExit = true }
                                (index + 1)
                        | _ when optionTakesValue optionName -> Invalid(sprintf "Missing value for %s." optionName)
                        | _ -> Invalid(sprintf "Unknown option '%s'." optionName)
                    else
                        loop (index + 1) (positional @ [ argument ]) options

            loop 0 [] Defaults.analysisOptions
