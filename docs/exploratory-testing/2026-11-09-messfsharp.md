# Exploratory testing report: CLI configuration and CI

Date: 2026-11-09

Build: `origin/main` at `efee9babc2ab3e4dde7a8381b2637f56075a432f`, version `0.1.17`, Release, .NET SDK `10.0.302` on Linux. Dependencies were restored with `dotnet restore messfsharp.sln --locked-mode` in a fresh worktree. The SDK, NuGet packages, build outputs, and test data were isolated in that worktree.

The [CLI transcript](2026-11-09-evidence/cli.log) records the commands, outputs, and exit codes. Two runs from a newly created fixture directory produced identical transcripts. The [comparison transcript](2026-11-09-evidence/comparison.log) checks report contents with and without exit-code overrides. The [replay script](2026-11-09-evidence/replay.sh) contains the fixture contents. To replay it, build the Release CLI, copy the script to `.explore/run.sh`, and adjust its SDK and home environment paths if necessary. The script replaces `.explore/case`.

## Journeys

### Gate a CI scan and retain the report

The user wants valid findings and parse errors in a report, with an exit code that CI can interpret. `docs/usage.md` promises independent parsing and report replacement.

- A valid source file with a mutable module variable produced one `GlobalVariable` finding and exit 2.
- Scanning that file alongside a malformed file replaced a pre-existing sentinel report with JSON containing both the finding and two parse errors. The command exited 1.
- `--ignore-errors-on-exit` changed the mixed scan's exit to 2. A separate error-only comparison changed exit 1 to 0 without changing report bytes. `--ignore-violations-on-exit` changed a finding-only scan from exit 2 to 0, also without changing report bytes.

### Suppress an intentional exception

The user wants a documented exception to stop failing CI, while retaining a way to review it. A `SuppressMessage("messfsharp", "GlobalVariable")` attribute suppressed the finding and returned exit 0. With `--strict`, the same file reported the finding at line 4 and returned exit 2. The report did not claim a suppression marker, consistent with the current README.

### Tune a team ruleset

The user wants a custom threshold to affect findings. A custom XML ruleset with `LongVariable` maximum 8 reported `extraordinaryLongVariableName` and returned exit 2. Changing the maximum to 40 produced no findings and exit 0. The ruleset file was read again on the second invocation.

## Findings and limits

No product bugs were confirmed in these journeys. No new issue was filed. Existing [#232](https://github.com/quality-gates/messfsharp/issues/232) tracks a script parsing gap outside these fixtures. Existing [#164](https://github.com/quality-gates/messfsharp/issues/164) tracks the ruleset alias behavior previously reported as #204. Neither was retested here.

The full `dotnet test tests/MessFSharp.Tests/MessFSharp.Tests.fsproj -c Release --no-restore` run passed 248 tests and failed the timing assertion `reference resolution scales to hundreds of declarations sharing one name`. An isolated rerun failed the same assertion. This is an unresolved test-environment or performance observation, not a confirmed CLI defect. No baseline comparison or performance investigation was performed.

The pass used small synthetic files and JSON output. Other formats, large projects, `.fsi` files, and actual CI hosting were not exercised. No application server, browser, container, image, volume, or listening port was started. The .NET build started an MSBuild worker, which was stopped with `dotnet build-server shutdown`.
