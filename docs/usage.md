# Usage

Command shape:

```console
messfsharp <paths> <format> <ruleset[,ruleset...]> [options]
```

`paths` accepts comma-separated files and directories. Directories are walked
recursively in deterministic normalized-path order. `.fs`, `.fsi`, and `.fsx`
are included by default; `bin`, `obj`, `.git`, and `node_modules` are always
skipped. Parsing is independent per file, so one malformed file does not hide
findings in valid files.

Formats: `text`, `xml`, `json`, `html`, `ansi`, `github`, `gitlab`,
`checkstyle`, `sarif`. Reports contain the tool name, version, stable rule
names, locations, priorities, context, and processing errors. `--reportfile`
writes the complete report and replaces an existing file.

## Examples

```console
messfsharp src text fsharp --ignore-tests
messfsharp src text fsharp,opinionated --ignore-tests
messfsharp src sarif fsharp --ignore-tests --reportfile reports/messfsharp.sarif
messfsharp src github fsharp --ignore-tests
messfsharp src json fsharp,codesize,design --only CyclomaticComplexity
```

## Options

- `--minimumpriority n` retains priorities `<= n`; `--maximumpriority n`
  retains priorities `>= n` (priority 1 is highest).
- `--suffixes .fs,.fsi` replaces the discovery suffix list.
- `--exclude generated,legacy` excludes paths containing those values.
- `--ignore-tests` skips `Test.fs`, `Tests.fs`, `Test.fsx`, `Tests.fsx`, and
  directories ending in `Tests` or `.Tests`.
- `--only` and `--enable` select named rules already loaded by the rulesets;
  `--disable` subtracts them.
- `--strict` includes findings suppressed on declarations with standard
  `SuppressMessage` metadata. Without it, those intentional exceptions are
  omitted.
- `--color` colorizes text; `ansi` always emits ANSI styling; `--verbose` /
  `-v` prints ruleset diagnostics.
- `--ignore-errors-on-exit` and `--ignore-violations-on-exit` change only the
  final process code; report content is unchanged.

`--help`, `-h`, and `help` print usage. `--version` prints
`messfsharp <version>`.

## Exit codes

| Code | Meaning |
| :--: | :--- |
| **0** | Run completed without selected violations |
| **1** | Argument, discovery, parsing, processing, rendering, or report-write error |
| **2** | Run completed with one or more violations |

Processing errors take precedence over violations unless the corresponding
ignore-on-exit option is used.

## Install variants

Global tool:

```console
dotnet tool install --global messfsharp
```

Repo-local tool:

```console
dotnet new tool-manifest
dotnet tool install messfsharp
dotnet tool run messfsharp src text fsharp --ignore-tests
```

From a checkout:

```console
dotnet run --project src/MessFSharp -- src text fsharp --ignore-tests
```
