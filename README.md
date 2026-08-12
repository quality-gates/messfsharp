# messfsharp

Catch maintainability problems in F# before they calcify: oversized functions
and types, tangled dependencies, dead private code, muddy naming, and other
mess that reviews keep rediscovering.

`messfsharp` is a local CLI. It scans ordinary `.fs`, `.fsi`, and `.fsx` source
with FSharp.Compiler.Service; the target project does not need to restore,
build, or run. Modules, records, discriminated unions, pipelines, and
expression-oriented control flow are interpreted in F# terms. .NET 10 SDK.

## Quick start

```console
dotnet tool install --global messfsharp
messfsharp src text fsharp --ignore-tests
```

That scans `src` with the recommended low-noise policy and prints findings on
stdout. Exit `0` is clean, `2` means findings, `1` means the tool or a source
file failed.

Common next steps:

```console
messfsharp src text fsharp,opinionated --ignore-tests
messfsharp src sarif fsharp --ignore-tests --reportfile reports/messfsharp.sarif
messfsharp src github fsharp --ignore-tests
```

Full command syntax, options, and discovery: [docs/usage.md](docs/usage.md).
Ruleset membership and defaults: [docs/rulesets.md](docs/rulesets.md).
Custom XML schema: [docs/ruleset-schema.md](docs/ruleset-schema.md).

## Install

```console
dotnet tool install --global messfsharp
messfsharp --version
```

Pin it for a repository or CI job:

```console
dotnet new tool-manifest
dotnet tool install messfsharp
dotnet tool run messfsharp --version
```

## Tune the gate

Start with `fsharp`. Add `opinionated` when you want the stricter checks the
recommended set leaves out. Point at a custom XML ruleset when thresholds or
membership need to live in the repo — see [docs/ruleset-schema.md](docs/ruleset-schema.md).

```console
messfsharp src text path/to/team-policy.xml --ignore-tests
```

## Suppress one intentional exception

Use standard `SuppressMessage` metadata on the declaration:

```fsharp
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "RuleName")>]
module Deliberate =
    let value = 1
```

Without `--strict`, suppressed findings are omitted from the report. With
`--strict`, they stay visible and marked suppressed.

## Drop it into CI

```yaml
# GitHub Actions
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: "10.0.302"
- run: dotnet tool install --global messfsharp
- run: messfsharp src github fsharp --ignore-tests
```

```yaml
# GitLab Code Quality
script: messfsharp src gitlab fsharp --reportfile gl-code-quality-report.json
artifacts:
  reports:
    codequality: gl-code-quality-report.json
```

This repository also self-checks after building. A finding fails the job with
exit code `2`.

## Maintainers

Command reference: [docs/usage.md](docs/usage.md).
Release process: [docs/releasing.md](docs/releasing.md).

Development checks:

```console
dotnet test tests/MessFSharp.Tests/MessFSharp.Tests.fsproj -c Release
```

## License

MIT. See [LICENSE](LICENSE).
