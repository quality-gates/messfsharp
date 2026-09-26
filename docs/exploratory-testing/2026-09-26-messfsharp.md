# Exploratory testing report: messfsharp CLI pass

Date: 2026-09-26

Repository: `quality-gates/messfsharp`

Scope: `main` at `v0.1.9` (`18c85ee`), exercised across the CLI, configuration options, report formats, and rule execution.

Build under test: `0.1.9`, Release, .NET SDK `10.0.302`, FSharp.Compiler.Service `43.11.302`.

Evidence: Raw evidence and reproducer fixtures are preserved locally under `artifacts/exploratory-testing/2026-09-26-messfsharp/`. This includes journey fixtures, format reports, minimised repros, and an automated verification script `artifacts/exploratory-testing/2026-09-26-messfsharp/replay.sh`.

## Starting state and readiness

- Repository was clean on `main` at commit `18c85ee`.
- Test suite executed via `dotnet test tests/MessFSharp.Tests/MessFSharp.Tests.fsproj -c Release` with 183 tests passing (0 failed, 0 skipped).
- CLI binary was built to a scratch directory (`target/build/messfsharp.dll`) and driven directly via `dotnet target/build/messfsharp.dll`. Scratch build outputs were cleaned up after testing.

## Journeys exercised

### 1. CLI options, configuration, and error contracts

- **Ordinary path:** Scanned single and multi-file projects with standard rulesets (`fsharp`, `codesize`, `naming`, `unusedcode`, `design`, `cleancode`, `explicitness`). Clean source files exited 0 with no findings; files with known violations exited 2 with correctly attributed line numbers and descriptions.
- **Path and discovery options:**
  - `--basedir`: Correctly rendered report paths relative to the specified base directory.
  - `--ignore-tests`: Accurately skipped `*Tests` directories and `Tests.fs` files while retaining standard production source files.
  - `--exclude`: Filtered out paths matching specified substring tokens.
  - `--suffixes`: Successfully restricted scanning to specified file extensions (e.g. `.fsx`).
- **Rule filtering:**
  - `--only` / `--enable`: Limited findings strictly to the specified rule names.
  - `--disable`: Subtracted specified rules from loaded rulesets.
  - `--ignore-violations-on-exit`: Changed exit code from 2 to 0 while keeping the complete report on stdout/reportfile.
- **Custom XML ruleset handling:**
  - Loaded custom XML rulesets and verified property overrides (e.g. `LongVariable` maximum length overridden to 10).
  - Malformed XML and non-existent XML files failed fast with exit code 1 and descriptive diagnostics.
  - Missing CLI arguments and unknown report formats printed usage guides and exited 1.

### 2. Multi-format CI and tooling reporting

All 9 supported report formats were generated and validated against both stdout and `--reportfile`:
- `text`: Verified standard colon-delimited format (`file:line:Rule: message`).
- `json`: Verified schema validity via `jq` and Python; confirmed fields `tool`, `version`, `errors`, and `violations`.
- `sarif`: Verified valid SARIF v2.1.0 schema with runs, driver information, and result locations.
- `gitlab`: Verified valid JSON array of Code Quality issue objects (`description`, `check_name`, `fingerprint`, `severity`, `location`).
- `xml`: Parsed and validated XML tree root `<report>` and child elements using `xml.etree.ElementTree`.
- `checkstyle`: Parsed and validated standard `<checkstyle>` XML schema.
- `html`: Validated standalone HTML document structure.
- `ansi`: Verified terminal ANSI styling escapes.
- `github`: Verified standard GitHub Actions workflow commands (`::warning file=...`).
- `--reportfile`: Confirmed that parent directories are automatically created if they do not exist, and existing report files are overwritten cleanly.

### 3. Deep semantic/syntactic analysis on idiomatic F# constructs

Exercised modern and advanced F# syntax against the rules engine:
- **Language constructs:** Computation expressions (`async { ... }`, `task { ... }`), active patterns (`(|Even|Odd|)`), discriminated unions, anonymous records (`{| X = 1 |}`), string interpolation (`$"..."` and triple-quoted `$$"""{{{...}}}"""`), array slicing (`data[0..2]`), UTF-8 literals (`"..."u8`), struct tuples (`struct (1, 2)`), point-free composition (`>>`), object expressions (`{ new IDisposable with ... }`), and secondary class constructors.
- **Unused code:** Verified that identifiers referenced within string interpolation and multiline interpolated expressions are recognized as used.
- **Class cohesion & coupling:** Verified `LackOfCohesionOfMethods` (LCOM4) on disconnected classes, and `BooleanGetMethodName` on boolean members.
- **Explicitness:** Verified that object expression mutations of outer local bindings are treated as local mutations, while mutations of module-level mutables from object expressions are properly reported as `ImplicitOutput`.

## Confirmed bugs filed

### 1. `ImplicitInput` and `ImplicitOutput` report shadowed ambient identifiers as ambient I/O violations (False Positive)
- **Issue:** [#155](https://github.com/quality-gates/messfsharp/issues/155)
- **Rules:** `ImplicitInput`, `ImplicitOutput` (`explicitness` ruleset)
- **Impact:** Any function accepting a logger, stream, or helper named `printfn`, `eprintfn`, `stdin`, `stdout`, or `stderr` as a parameter, or declaring a local helper/binding with those names, is falsely flagged as performing ambient I/O.
- **Repro:** `artifacts/exploratory-testing/2026-09-26-messfsharp/repros/ambient-shadow-param.fs`
  ```fsharp
  module Repro

  let log (printfn: string -> unit) =
      printfn "hello"
  ```
- **Expected:** Exit code 0, no findings. `printfn` is an explicit formal parameter.
- **Actual:** `Repro.fs:4:ImplicitOutput: 'log' performs ambient output 'printfn' instead of returning data.`
- **Root cause:** In `Flows.fs:326-351` (`ambientAccess` and `expressionAccess`), matching identifiers immediately become `Ambient` flow facts and are classified without checking `resolve current identifiers accessRange` against `Bound` or `Parameter` flow facts.

### 2. `ElseExpression` does not recognize standard F# terminating functions (False Negative)
- **Issue:** [#156](https://github.com/quality-gates/messfsharp/issues/156)
- **Rule:** `ElseExpression` (`cleancode` ruleset)
- **Impact:** Functions ending a `then` branch with common F# Core terminating operators `failwithf`, `invalidArg`, `invalidOp`, `nullArg`, or `reraise` are not reported when an unflattened `else` branch follows.
- **Repro:** `artifacts/exploratory-testing/2026-09-26-messfsharp/repros/else-failwithf.fs`
  ```fsharp
  module Repro

  let checkFailwithf x =
      if x < 0 then
          failwithf "negative %d" x
      else
          x * 10
  ```
- **Expected:** `ElseExpression` reported on the flattenable `else` branch.
- **Actual:** Exit code 0, no findings.
- **Root cause:** In `Rules.fs:312-316`, `isTerminatingIdentifier` only checks `failwith` and `raise`.

### 3. `GlobalVariable` fails to observe mutations via `incr` and `decr` ref cell operators (False Negative)
- **Issue:** [#157](https://github.com/quality-gates/messfsharp/issues/157)
- **Rule:** `GlobalVariable` (`design` ruleset)
- **Impact:** Module-level `ref` cells that are mutated via standard F# `incr` or `decr` prefix operators are not recognized as mutated (when `report-immutable=false`), resulting in false negatives.
- **Repro:** `artifacts/exploratory-testing/2026-09-26-messfsharp/repros/global-var-incr-decr.fs`
  ```fsharp
  module Repro

  let counter = ref 0

  let step () =
      incr counter
  ```
- **Expected:** `GlobalVariable: Module-level shared value 'counter' is mutable or globally visible.`
- **Actual:** Exit code 0, no findings.
- **Root cause:** In `Rules.fs:250-270`, `mutationObserved` only inspects subsequent tokens for `<-`, `:=`, or `.Value <-`. Unlike `Flows.fs:366-369` (which handles `incr` and `decr`), `mutationObserved` does not check for preceding `incr`/`decr` operators.

## Usability observations

1. **`DevelopmentCodeFragment` case-insensitivity on raw source lines:**
   Because `unwanted-functions` (default `TODO,FIXME,HACK,Debug.Assert`) matches case-insensitively using regex word boundaries against all lines, standard domain models with types such as `type Todo = { ... }` or function arguments like `let processTodo (todo: Todo) = ...` are flagged with `Development-only marker found in production source`. Restricting `DevelopmentCodeFragment` to comments or non-declaration tokens would avoid flagging domain terms.

2. **`DuplicatedArrayKey` coverage:**
   `DuplicatedArrayKey` detects duplicate keys in `Map.ofList`, `Map.ofArray`, `Map.ofSeq`, and `dict [...]`, but does not inspect `readOnlyDict [...]`, which is also a standard F# Core collection constructor.
