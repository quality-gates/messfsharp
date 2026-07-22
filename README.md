# messfsharp

`messfsharp` is an F#-native mess detector distributed as a portable .NET
tool. It scans ordinary `.fs`, `.fsi`, and `.fsx` source directly with
FSharp.Compiler.Service; the target project does not need to restore, build,
or execute. Modules, records, discriminated unions, pipelines, pattern
matching, curried functions, immutable values, and explicit .NET interop are
interpreted in F# terms.

## Install

Install the released tool globally:

```shell
dotnet tool install --global messfsharp
messfsharp src text fsharp
```

Pin it for a repository or CI job:

```shell
dotnet new tool-manifest
dotnet tool install messfsharp
dotnet tool run messfsharp src json fsharp --reportfile artifacts/messfsharp.json
```

## Command

```text
messfsharp <paths> <format> <ruleset[,ruleset...]> [options]
```

`paths` accepts comma-separated files and directories. Directories are walked
recursively in deterministic normalized-path order. `.fs`, `.fsi`, and `.fsx`
are included by default; `bin`, `obj`, `.git`, and `node_modules` are always
skipped. Parsing is independent per file, so one malformed file does not hide
findings in valid files.

Formats are `text`, `xml`, `json`, `html`, `ansi`, `github`, `gitlab`,
`checkstyle`, and `sarif`. Reports contain the tool name, version, stable rule
names, locations, priorities, context, and processing errors. `--reportfile`
writes the complete report and replaces an existing file.

Options:

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
- `--color` colorizes text, `ansi` always emits ANSI styling, and `--verbose`
  or `-v` prints ruleset diagnostics.
- `--ignore-errors-on-exit` and `--ignore-violations-on-exit` change only the
  final process code; report content is unchanged.

`--help`, `-h`, and `help` print usage. `--version` prints
`messfsharp <version>`.

## Exit codes

`0` means the run completed without selected violations. `1` means an argument,
discovery, parsing, processing, rendering, or report-write error. `2` means
the run completed and found one or more violations. Processing errors take
precedence over violations unless the corresponding ignore-on-exit option is
used.

## Rulesets and configuration

The recommended ruleset is `fsharp`. Component rulesets are `codesize`,
`naming`, `unusedcode`, `cleancode`, `design`, and `controversial`;
`opinionated` makes the deliberately stricter F# checks explicit. See
[`docs/rulesets.md`](docs/rulesets.md) for membership and defaults, and
[`docs/ruleset-schema.md`](docs/ruleset-schema.md) for custom XML references,
exclusions, priorities, and properties.

The analyzer recognizes F# naming roles, backtick identifiers, operators,
generic parameters, active patterns, compiler-generated names, intentional
underscore bindings, immutable module values, mutation operators, records,
unions, guards, and expression-oriented `if/then/else`. It does not execute
scripts or resolve target-project packages.

## CI usage

The repository workflow builds and tests the executable, checks formatting and
compiler warnings, audits NuGet dependencies, and runs the freshly built tool
over production source. A typical project gate is:

```shell
dotnet run --project path/to/messfsharp.fsproj -- src json fsharp,codesize,design --ignore-tests --reportfile artifacts/quality.json
```

For a packaged smoke test, install the `.nupkg` into an isolated tool location,
run `messfsharp --version`, then analyze a fixture in JSON or SARIF format.

## Release and versioning

The package identifier and installed command are both `messfsharp`. The CLI and
structured reports read the assembly informational version, so release
versioning has one source of truth. Stable `vMAJOR.MINOR.PATCH` tags drive the
verified build, pack, isolated-install smoke test, checksum, and immutable
GitHub release workflow. NuGet publication is deliberately the final gated
step and requires repository-managed credentials.

## License

MIT. See [LICENSE](LICENSE).
