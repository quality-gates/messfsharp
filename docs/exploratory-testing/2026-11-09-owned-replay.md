# Exploratory testing report: owned CLI replay

Date: 2026-11-09

This pass repeats the three journeys in the [earlier CLI report](2026-11-09-messfsharp.md) with an explicit cleanup owner. The earlier pass stopped its MSBuild worker but had not registered it before startup and had no exit trap. This pass does not erase that limitation.

## Starting state and cleanup owner

The worktree started at `origin/main` commit `8f240e8bd0699a904afe07b81326790b65f92bee`. The CLI was built in Release with SDK `10.0.302`, using `dotnet restore messfsharp.sln --locked-mode`. SDK files, NuGet packages, the CLI build, and fixtures were inside the worktree. The second recorded owner run reused the SDK and restored packages from the first run in this worktree.

Before launching each command, [owner.py](2026-11-09-owned-evidence/owner.py) registered its intent and cleanup policy. It blocked `SIGINT` and `SIGTERM` across process launch and PID registration, then restored the signal mask. Each command ran in a separate process group. The owner recorded its PID and group ID and checked that no live group members remained after the command. The owner disables MSBuild server reuse and node reuse. Its `finally` block also terminates and checks any group still registered when the owner exits, including on `SIGINT` or `SIGTERM`. No browser, application server, listening port, container, image, or volume was started.

The [normal-run log](2026-11-09-owned-evidence/owner.log) records the registrations and empty groups. The [interruption log](2026-11-09-owned-evidence/interruption.log) records a self-test that sent `SIGTERM` between launching `sleep` and registering its PID. The signal was deferred, and cleanup verified that the child group was empty. A separate [process-list check](2026-11-09-owned-evidence/process-check.log) found no worktree-specific SDK process after the run. The owner cannot clean up after an uncatchable `SIGKILL` or host failure.

## Journeys and outcomes

Both [CLI transcripts](2026-11-09-owned-evidence/run-1.log) were byte-identical. The [second transcript](2026-11-09-owned-evidence/run-2.log) and the [report comparison](2026-11-09-owned-evidence/comparison.log) are retained. The earlier report's [replay script](2026-11-09-evidence/replay.sh) supplies the fixture contents and commands.

1. **Gate a CI scan and retain the report.** A mutable module variable produced one `GlobalVariable` finding and exit 2. A directory containing that file and a malformed file replaced a sentinel report with both the finding and parse errors, then exited 1. The error and violation exit overrides changed exit codes without changing the corresponding report bytes. [compare.py](2026-11-09-owned-evidence/compare.py) records the comparison commands.
2. **Suppress an intentional exception.** `SuppressMessage` removed the finding and returned exit 0. `--strict` restored it at line 4 and returned exit 2.
3. **Tune a team ruleset.** A custom `LongVariable` maximum of 8 reported the long variable and returned exit 2. A maximum of 40 produced no finding and returned exit 0.

No new product bugs were confirmed or filed. Existing [#232](https://github.com/quality-gates/messfsharp/issues/232) and [#164](https://github.com/quality-gates/messfsharp/issues/164) were not retested. The prior report's timing-test failure remains unresolved and was not rerun in this pass. The pass used synthetic files and JSON reports; other formats, large projects, `.fsi` files, and hosted CI integration were not exercised. The single-rule examples make the selected findings easy to inspect, but do not establish behavior for every ruleset.
