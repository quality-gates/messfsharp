# Coding standards

## Tests

- Strongly prefer integration tests and end-to-end tests over unit tests.
- Strongly prefer exercising real system behaviour over "the tests pass so it must work."
- Only mock third-party services we cannot control. Do not mock code we own.
- For this codebase, the default proof is: run the real CLI/analyzer on real (or fixture) source and assert findings, exit codes, and report output.

## Comments and docs

- Code comments use ASD-STE100 Simplified Technical English.
- Ground terms in `CONTEXT.md` domain language when that file exists. Do not invent synonyms for glossary terms.
- Do not write comments that only repeat what the code already makes clear.
- Do not put brittle references in README or comments (versions, line numbers, temporary paths, "as of today" claims) when those details are allowed to change.

## Common footguns

- Tautological tests (asserting the mock was called the way the test just configured it).
- Mocks of modules/services we own.
- "Green suite" treated as proof the product works for a user.
- Narrating comments and README drift magnets.
- Cheating complexity or quality gates with denser syntax, hidden branching, or indirection that does not reduce real complexity.

## F#

- Prefer total, immutable style: `let`-bound values, discriminated unions, records. Use `mutable` / ref cells only at proven edges (e.g. accumulators in tight local loops) and keep them private.
- Organize with modules and small types under `src/MessFSharp/` (`Cli`, `Engine`, `Model`, `SyntaxModel`, `Reports`, `Rulesets`, …). Prefer new modules over growing god-modules.
- Prefer explicit `Result` / `option` flows over exceptions for expected failure paths (bad input, missing ruleset, parse failure).
- Match repo formatting: 4-space indent for `.fs` (see `.editorconfig`). Keep deterministic build flags from `Directory.Build.props` intact.
- Treat warnings as errors in CI posture — do not introduce warning debt locally that CI will fail.
- Parse and model F# through the existing syntax/model pipeline. Do not add a second parsing stack or drop to raw string regex for structure that the model already represents.
- Keep F#-idiomatic exclusions and rules in the ruleset layer (`Rulesets`) rather than special-casing inside random callers.
- Tests live under `tests/MessFSharp.Tests` with fixtures in `tests/Fixtures`. Prefer acceptance/CLI/analyzer tests that run the real pipeline on fixture sources.
- Avoid object-oriented ceremony (deep inheritance, wide mutable classes) unless wrapping a .NET API that forces it.
- Do not bypass complexity or clarity with dense point-free pipelines or multi-step operators that obscure data flow. Prefer named local functions when a pipeline needs a comment to be readable.
