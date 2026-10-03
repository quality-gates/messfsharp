# Exploratory testing report: messfsharp configuration, suppression, and CI pass

Date: 2026-10-03

Repository: `quality-gates/messfsharp`

Scope: `main` at `0.1.14` (`b1df03c`). The pass drove the public CLI through three user journeys: tailoring rules with custom XML, suppressing intentional exceptions, and wiring messfsharp into CI against real open-source F# code.

Build under test: `0.1.14`, Release, .NET SDK `10.0.302`.

Evidence: Raw evidence is preserved locally (gitignored) under `artifacts/exploratory-testing/2026-10-03-messfsharp/`. It contains:

- journey fixtures (`fixtures/j1`–`j5`)
- reports in every format (`out/j3`)
- corpus scan output (`out/j4`)
- delta-debugging scripts and minimised repros (`ddmin*.py`, `repros/`)
- `replay.sh`, which rebuilds every confirmed bug from a fresh temporary directory
- two identical replay logs (`replay-1.log`, `replay-2.log`)

## Starting state and readiness

- The repository was clean on `main` at `b1df03c`.
- The CLI was built in Release to the evidence directory and driven through a wrapper `mf` (`dotnet build/messfsharp.dll "$@"`).
- `0.1.9` and `0.1.13` builds were used to check whether bugs were regressions.
- The corpus was shallow clones of `fsprojects/Argu` (`8dcad5b`), `giraffe-fsharp/Giraffe` (`279fe3a`) and `fsprojects/FSharp.Data.Adaptive` (`76afc00`).

## Journeys exercised

### 1. Tailor rules for a team with a custom XML ruleset

Goal: a team ruleset that tightens selected thresholds and excludes noisy rules, with the effect visible in findings.

- **Ordinary path:**
  - Referenced `rulesets/codesize.xml` with `<exclude>`, a priority override, and a `LongVariable` `maximum=8` override.
  - Exclusions, priorities and later-wins deduplication behaved as documented.
- **Variation:**
  - Used `reportLevel` instead of `maximum` on a bulk reference.
  - This also lowered the thresholds of `ExcessiveMethodLength` and `ExcessiveClassLength`, which document no `reportLevel` ([#204](https://github.com/quality-gates/messfsharp/issues/204)).
- **Error handling:** An unknown rule and a bogus ruleset reference produced warnings under `-v`.
- **CLI filters:** `--only`, `--enable`, `--disable`, `--minimumpriority` and `--maximumpriority` were consistent with the XML.

### 2. Suppress an intentional exception with `SuppressMessage`

Goal: a reviewed exception stops failing CI, while `--strict` still reveals it.

- **Ordinary path:** Positional `SuppressMessage("messfsharp", "Rule")` suppressed findings at function, top-level module, namespace module and type scope. `--strict` revealed the findings again.
- **Variations that worked:** the `SuppressMessageAttribute` suffix, a `Justification` argument, extra whitespace, and multiple attributes.
- **Variations that failed:**
  - A lowercase rule name was not honoured, despite the documented case-insensitivity ([#205](https://github.com/quality-gates/messfsharp/issues/205)).
  - The named-argument form `category = ..., checkId = ...` was ignored ([#206](https://github.com/quality-gates/messfsharp/issues/206)).

### 3. Gate CI on real code and read the results

Goal: annotations and reports a developer can act on, on real projects, without false alarms.

- **Formats:** Generated all nine formats for a path containing spaces, `&` and `<x>`, plus a file with a parse error and a missing input path.
  - XML, HTML and SARIF escaping was correct.
  - GitHub property escaping of `,` and `:` was correct.
  - Parse errors carried a location.
  - For a missing path, the `github` format dropped the path, unlike every other format ([#207](https://github.com/quality-gates/messfsharp/issues/207)).
- **Corpus:** Ran `messfsharp src json fsharp,opinionated --ignore-tests` on three projects.
  - Argu: 209 findings, 0 errors.
  - Giraffe: 167 findings, 0 errors.
  - FSharp.Data.Adaptive: 3632 findings, 4 errors, 179 s.
- **Unused-code triage:** I traced the `unusedcode` findings back to source and minimised them with delta debugging:
  - A `///` doc comment above a class makes its `let` fields read as unused. This affects Argu `Utils.fs` and `ParseResults.fs`, and was already present in `0.1.9` ([#208](https://github.com/quality-gates/messfsharp/issues/208)).
  - Locals in property bodies are reported unused. Methods named `Val…` (Giraffe `ValidatePreconditions`) and methods with ` with` on the member line are misclassified as properties and get the same false positive ([#209](https://github.com/quality-gates/messfsharp/issues/209)).
- **Parse errors:** I traced the four FSharp.Data.Adaptive errors back to source. The stock `dotnet new console -lang F#` `Program.fs` is rejected as a library file and the run exits 1 ([#210](https://github.com/quality-gates/messfsharp/issues/210)).

## Confirmed bugs filed

Each bug below was replayed twice from a fresh temporary directory with `replay.sh`, with identical output.

### 1. `reportLevel` leaks into rules without a `reportLevel` alias

- **Issue:** [#204](https://github.com/quality-gates/messfsharp/issues/204)
- **Impact:** Setting `reportLevel=5` on `rulesets/codesize.xml` reports every method or module longer than 5 lines.
- **Expected:** Exit 0. The length rules keep `minimum=100` and `minimum=1000`, as they do with `maximum=5`.
- **Actual:** `Method length 19 exceeds maximum 5 lines` and `Type or module length 24 exceeds maximum 5 lines` (exit 2).
- **Root cause:** `Rulesets.fs` `selection` copies `reportLevel` into `maximum`, `minimum`, `maxfields` and `maxmethods` for every rule.

### 2. Lowercase rule names in `SuppressMessage` are ignored

- **Issue:** [#205](https://github.com/quality-gates/messfsharp/issues/205)
- **Repro:** `SuppressMessage("messfsharp", "cyclomaticcomplexity")` on a function with complexity 11.
- **Expected:** Suppressed (exit 0), as with `CyclomaticComplexity`.
- **Actual:** `CyclomaticComplexity: Cyclomatic complexity 11 exceeds maximum 10.` (exit 2)
- **Root cause:** `Engine.isSuppressed` uses a case-sensitive `Set.Contains`.

### 3. Named-argument `SuppressMessage` is ignored

- **Issue:** [#206](https://github.com/quality-gates/messfsharp/issues/206)
- **Repro:** `SuppressMessage(category = "messfsharp", checkId = "CyclomaticComplexity")`.
- **Expected:** Suppressed (exit 0).
- **Actual:** The finding is reported (exit 2).
- **Root cause:** The suppression regex in `Model.fs` only matches two positional string literals.

### 4. `github` format omits the path of errors without a location

- **Issue:** [#207](https://github.com/quality-gates/messfsharp/issues/207)
- **Repro:** `messfsharp missingdir github naming`
- **Expected:** The annotation names `missingdir`.
- **Actual:** `::error title=messfsharp::Requested path does not exist.` (exit 1)
- **Root cause:** `GitHubReporter.fs` only emits `file=` when `Location` is `Some`.

### 5. A `///` doc comment above a class makes its fields read as unused

- **Issue:** [#208](https://github.com/quality-gates/messfsharp/issues/208)
- **Repro:**
  ```fsharp
  module M

  /// Returns true once a value is set.
  type Node() =
      let mutable hasValue = false
      member _.HasValue = hasValue
  ```
- **Expected:** Exit 0. The same file with a `//` comment is clean.
- **Actual:** `M.fs:5:UnusedPrivateField: Private field 'hasValue' is never used.` (exit 2)
- **Regression check:** Also present in `0.1.9` and `0.1.13`.

### 6. Locals in properties, and in methods misread as properties, are reported unused

- **Issue:** [#209](https://github.com/quality-gates/messfsharp/issues/209)
- **Repro:**
  ```fsharp
  module Shop

  type Calc() =
      static member Validate(x: int) =
          let doubled = x * 2
          doubled + 1
  ```
- **Expected:** Exit 0, as when the member is renamed to `Stamp`.
- **Actual:** `M.fs:5:UnusedLocalVariable: Local binding 'doubled' is never used.` (exit 2). The same happens for `member _.Total = let subtotal = 10 ...` and for a method with `match x with` on its first line.
- **Root cause:**
  - `Model.fs` classifies members as `Property` when the line contains `member val` (case-insensitive) or ` with`.
  - `referenceScope` does not treat `Property` as an enclosing scope.

### 7. Implicit-entry `Program.fs` cannot be analysed

- **Issue:** [#210](https://github.com/quality-gates/messfsharp/issues/210)
- **Repro:** the `Program.fs` generated by `dotnet new console -lang F#`. `dotnet build` of that project succeeds.
- **Expected:** Exit 0 with no errors.
- **Actual:** `Files in libraries or multiple-file applications must begin with a namespace or module declaration ...` (exit 1).
- **Root cause:** `Parsing.fs` leaves `FSharpParsingOptions.IsExe` false.

## Unresolved candidates

- **Scan time on FSharp.Data.Adaptive:** 179 s for 99 files with `fsharp,opinionated`. It was not profiled, so it is unclear whether this is a regression of the fix for #93 or expected cost for large files.
- **`ExitExpression` in Argu `Types.fs:90`** (`exit (int errorCode)`): this is probably intended behaviour and was not investigated.

## Rejected candidates

- **Backtick identifiers** were exempt from the naming rules. This is consistent with the rules' design, so no bug.

## Usability observations

These are observations; the suggested improvements are not filed.

1. **Duplicate error output:** In text mode, processing and ruleset errors print twice, once in the stdout report and once on stderr. A missing-rule error also lowercases the rule name (`'longvariable'`). Printing errors once, with the name as the user typed it, would read better.
2. **Dead help links:** SARIF `helpUri` values such as `https://github.com/quality-gates/messfsharp#cyclomaticcomplexity` point to README anchors that do not exist. Linking to the rule table in `docs/rulesets.md` would make them useful.
3. **HTTP verb constants:** On Giraffe, `CamelCaseVariableName` (`controversial`) flags HTTP verb constants such as `GET` and `POST`. That is consistent with the rule, but noisy for web code. An `exceptions` property like the one on `ShortVariable` would help.

## Unexplored areas

- `--ignore-errors-on-exit` combined with GitHub annotations.
- `.fsi` signature files.
- Performance profiling of the slow corpus scan.
