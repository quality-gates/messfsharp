# Exploratory testing report: explicitness rulesets

Date: 2026-09-23

Repository: `quality-gates/messfsharp`

Scope: `origin/main..62e319c` (`feat(rules): add explicitness and strictexplicitness rulesets`), exercised through the CLI.

Build under test: `0.1.7` + `62e319c`, Release, .NET SDK `10.0.302`, FSharp.Compiler.Service `43.11.302`.

Fix: `3169ce1` (`fix(rules): resolve explicitness module data by module path`), shipped in the same pull request as the rulesets.

Evidence: the raw evidence was kept locally under the ignored `artifacts/exploratory-testing/2026-09-23-explicitness/` directory. It includes the journey fixtures, minimisation variants, differential outputs and `replay.sh`, and file names below refer to that directory. The minimised repros are inlined below. The committed regression fixture is `tests/Fixtures/explicitness-modules.fs`.

## Starting state and readiness

- `main` was one commit ahead of `origin/main`. The only uncommitted file was the untracked `messfsharp-report-*.json`, which was left untouched.
- The CLI was built to a scratch `target/` directory and run as `dotnet target/build/messfsharp.dll <path> <format> <rulesets>`. After the pass, `target/` was deleted.
- Before the pass, the Release test suite passed with 180 tests. After the fix it passes with 181.

## Journeys exercised

1. **Find implicit inputs and outputs in a realistic module** (`fixtures/Cart.fs`, `fixtures/CartFixed.fs`).
   - Ordinary path: a cart module with module-level mutable state, `printfn`, `DateTime.Now` and argument mutation. Findings matched `docs/rulesets.md`.
   - Variation: the same module refactored to take state as arguments and return new values. The pure version was clean.
   - Argument mutation was detected for these parameters: `ResizeArray`, `string ResizeArray`, `HashSet`, arrays, `Dictionary` indexer set, and `ref`.
2. **Strict class rules** (`fixtures/Probe2.fs`, `fixtures/Probe3.fs`).
   - `strictexplicitness` was run over classes, records, interfaces, static members, type extensions and object expressions.
   - `let` fields, primary constructor arguments, `this.X` reads, field assignment and collection-field mutation were all reported.
   - Same-type method calls through the self identifier were skipped, as documented.
3. **Integration with existing CLI features** (`fixtures/custom.xml`, `fixtures/cc.xml`, `out.json`, `out.sarif`, `repo-scan.txt`).
   - Custom XML worked for `ref`, `exclude` and `priority`, and so did `--only`, `--minimumpriority`, and the text, JSON, SARIF and GitHub formats.
   - `fsharp` excludes the new rules, and ruleset names are case-insensitive.
   - `strictexplicitness` alone on a module-only file exits 0.
   - `SuppressMessage` works on functions, types, members and nested modules.
   - A full-repository scan took about 4.2 s. All findings were plausible; the `invalid.fs` parse errors are intentional fixtures.
   - `fixtures/Script.fsx` produced the expected `ImplicitInput` and `ImplicitOutput` for a script-level mutable. `fixtures/Sig.fsi` produced no errors and no findings; it contains signatures only.

## Confirmed bugs (fixed in `3169ce1`)

Before minimisation, each bug was replayed twice from a clean build with identical output (`repros/replay.txt`). Each was then minimised to the files under `repros/`; the cut variants that went green are under `minimisation/`.

To replay: the committed regression test ``explicitness resolves module data by module path and local function names`` covers all three bugs. Locally, `replay.sh` in the evidence directory does too. It builds the current checkout into a temporary directory and prints `GREEN`, or a `RED` line for each bug that reproduces. The same four checks, run from a scratch copy of this script, were RED at `62e319c`. `replay.sh` is GREEN at `3169ce1`.

### A: Qualified access to nested-module state is missed (false negative)

- **Impact:** a function that reads or mutates `State.count` or `State.items` is not reported, although the same access unqualified is. This is the common F# style of keeping state in a nested module.
- **Repro:** `repros/a-qualified-read.fs` and `repros/a2-qualified-container.fs`.
  ```fsharp
  module Repro
  module State =
      let mutable count = 0
  let read () = State.count
  ```
- **Expected:** `ImplicitInput` for `'read'` reading `State.count`. For the `a2` repro: `ImplicitOutput` for `'add'` writing `State.items`.
- **Actual at `62e319c`:** no findings, exit 0. Writes through `<-` were reported, so only reads and mutating calls were affected.

### B: A local function doesn't shadow a module mutable (false positive)

- **Impact:** calling a local helper whose name matches a module mutable is reported as reading that mutable.
- **Repro:** `repros/b-local-function.fs`.
  ```fsharp
  let mutable log = 0
  let run x =
      let log y = y + 1
      log x
  ```
- **Expected:** no findings. The docs say that locals shadow outer names.
- **Actual at `62e319c`:** `b-local-function.fs:7:ImplicitInput: 'run' reads mutable shared value 'log' ...`. The same happened for `let rec log` and for `let rec ... and` groups.

### D: Module data is keyed by bare name, so modules collide (false positive)

- **Impact:** an immutable value is reported as a mutable read when any other module declares a mutable with the same name.
- **Repro:** `repros/d-sibling-module.fs`.
  ```fsharp
  module Repro
  module A =
      let mutable count = 0
  let count = 5
  let get () = count
  ```
- **Expected:** no findings. `count` here is the immutable top-level value.
- **Actual at `62e319c`:** `d-sibling-module.fs:7:ImplicitInput: 'get' reads mutable shared value 'count' ...`. The reverse nesting and sibling nested modules were affected too (`minimisation/d-reverse.fs`, `minimisation/d-toplevelB.fs`, `fixtures/Probe6.fs`).

### Diagnosis and fix

The fix followed `/diagnosing-bugs`:

1. A feedback loop, now `replay.sh`, went red on all three bugs.
2. The repros were minimised.
3. Five ranked hypotheses were formed.
4. A single tagged probe (`[DEBUG-e7x1]`) printed facts and resolutions.
5. A regression test was written first and watched fail. It is ``explicitness resolves module data by module path and local function names``, with fixture `tests/Fixtures/explicitness-modules.fs`.
6. Cleanup: the probe is removed and a grep for the tag returns nothing.

Hypotheses:

| # | Hypothesis | Verdict | Evidence from the probe |
| --- | --- | --- | --- |
| H1 | Module data is keyed by bare name without its module path | **Confirmed** (A, D) | `ModuleData count false`, with no path; D resolved `count` to the mutable `A.count` |
| H2 | A candidate is created for a qualified access, but resolves to Unknown | **Confirmed** (A; a consequence of H1) | `Read ["State"; "count"] -> Unknown "State.count"` |
| H3 | No candidate is created for a qualified access | Rejected | The candidate was present |
| H4 | Local function heads are never bound as locals | **Confirmed** (B) | Only `Bound y` was emitted; `log` was not bound |
| H5 | The local binding exists but has the wrong scope | Rejected | No binding existed |

The fix is in `src/MessFSharp/Flows.fs`:

- Module values are keyed by their enclosing module path.
- A name resolves from the innermost enclosing module outwards, then through `open` declarations visible to the owner.
- Local function heads are bound for the `let` body, or for the whole `let rec` group.
- `docs/rulesets.md` describes the resolution order and the remaining limitation.

Verification:

- The regression test passes.
- The full suite passes 181/181.
- `dotnet build -warnaserror` is clean.
- `fantomas --check` passes on `src` and `tests/MessFSharp.Tests`.
- The self-check (`src/MessFSharp text fsharp,codesize,design --ignore-tests`) exits 0.
- **Differential run** (`differential/`): every fixture here plus a scan of `src,tests` was run with both rulesets at `62e319c` and at the fix. Every line in `differential/diff.txt` is one of the intended A/B/D changes. The other findings (Cart, Probe2–4, the repository scan) are identical.

## Unresolved

- **C: inherited or foreign methods through the self identifier are reported as type data** ([#146](https://github.com/quality-gates/messfsharp/issues/146)) (`fixtures/Probe3.fs:8`, `fixtures/Probe2.fs:35`, still present at `3169ce1`).
  - Two cases are reported as `ImplicitClassInput: ... reads type data 'Helper'` (or `'ToUpper'`):
    - `this.Helper x`, where `Helper` is declared on a base class.
    - `this.ToUpper()` inside a `String` type extension.
  - `base.Helper x` is not reported.
  - The docs only promise that methods declared on the *same* type are skipped. Without type information, a method can't be told apart from a property, so this is a product decision rather than a clear bug. The options are to skip any `this.X` applied to arguments, or to keep reporting it and document the behaviour.

## Rejected candidates and observations

- **`List<T>`, `IList<T>` and `ICollection<T>` parameters are not treated as mutable containers.** In F#, `List<T>` without `open System.Collections.Generic` is the immutable list, and the syntax can't tell the two apart. The docs say "and similar", which is a usability observation, not a bug. A suggested improvement is to name the recognised types in the docs.
- **Object expressions in module-level values are not checked.** This is documented: "Module-level values that are not functions ... have no owner".
- **The SARIF `rules` array lists only rules that have results.** This is existing behaviour shared by all rulesets, not specific to this change.

## Limitations

- **`open` shadowing is not modelled.** A name declared in the current module wins over an `open`ed module, even when the `open` comes later and F# would pick the opened value. The known false negative is `minimisation/open-shadow.fs`, where `count` after `open State` is not reported. This is documented in `docs/rulesets.md` and tracked in [#147](https://github.com/quality-gates/messfsharp/issues/147).
- The analysis is untyped, so type aliases, module aliases (`module S = State`) and `[<AutoOpen>]` modules are not resolved. This was not probed exhaustively.
- Only the local Release build was tested. A packed tool install, Windows paths and very large trees were not tested.

## Issue tracker

- A, B and D were not filed. They existed only in the unreleased commit `62e319c` and were fixed in `3169ce1`, before the rulesets were first pushed.
- C is filed as [#146](https://github.com/quality-gates/messfsharp/issues/146), labelled `bug` and `needs-triage`, because it needs a product decision.
- The `open` shadowing false negative is filed as [#147](https://github.com/quality-gates/messfsharp/issues/147), labelled `bug` and `ready-for-agent`.
