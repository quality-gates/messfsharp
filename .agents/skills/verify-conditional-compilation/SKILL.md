---
name: verify-conditional-compilation
description: Verify conditional-compilation diagnostics when changing the parser, source masking, scanner, declaration model, naming rule, or compiler settings.
---

# Verify conditional compilation

The analyzer must report a naming violation for each active generated declaration, and none for an inactive declaration, at its original line. A mismatch can falsely fail a quality gate or hide a finding.

## Run the check

From the repository root, run:

```sh
.agents/skills/verify-conditional-compilation/scripts/check.sh
```

The command builds the current source and runs the real `Engine.analyzeSource` with `CamelCaseVariableName`. It exits 0 only when every generated case has exactly the expected finding lines. It prints a counterexample for each source kind with failures. To run one case, pass its printed ID, for example:

```sh
.agents/skills/verify-conditional-compilation/scripts/check.sh --case fsi-compiled-flat
```

Use `--list-failures` to print each failing case, its expected lines, and its actual outcome on a `FAIL-RESULT` line. Save and compare those lines before and after a change to spot new failures or changed outcomes in existing failures. The command still exits nonzero if any case fails.

The check uses the .NET SDK pinned by `global.json` and FSharp.Compiler.Service pinned by the project and package lock. At the sync commit these are SDK 10.0.302 and compiler service 43.11.302. Restore requires the locked NuGet packages.

## Interpret a failure

Read the numbered source and compare the expected and actual finding lines. The generator uses an independent truth table: `ABSENT` is undefined, `COMPILED` is defined for `.fs` and `.fsi`, and `INTERACTIVE` is defined for `.fsx`. A leading `!` inverts the condition. These defaults match `Parsing.parse` and the F# compilation modes described in the [compiler directives reference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/compiler-directives).

If the generated source or truth table no longer matches the intended parser options, correct the generator and rerun. Otherwise report the source, expected findings, and actual findings as a product defect. Treat parser errors and build errors as failures to check, not as a pass. Do not turn a product failure into an expected success.

At sync commit `5a78d17eff0985400505e3ebbc17a21eda24a30a`, 466 of 903 cases fail. For `fsi-compiled-flat`, line 4 is active and line 6 is inactive. The analyzer reports a finding on line 6 and misses the one on line 4. The `.fs` and `.fsx` cases can also report inactive declarations. These are existing findings, so a nonzero exit remains the correct result until the product is fixed.

## Scope and synchronization

The checker exhausts 903 generated sources. It combines three source kinds with six conditions based on `ABSENT`, `COMPILED`, and `INTERACTIVE`. It covers an unconditional control, an `#if` without `#else`, a flat `#if` and `#else`, one nested conditional in either outer branch, and nested conditionals in both outer branches. The maximum nesting depth is two. Every branch has one generated value declaration, and active declarations before and after the conditional check line preservation.

This is bounded exhaustive testing of the actual F# analyzer, not an unbounded proof. It does not cover arbitrary F# text, compound conditions, other defined symbols, comments and strings containing directives, other diagnostic rules, discovery, formatting, or CLI exit codes. The generated examples use LF line endings. A passing result covers only the stated grammar and assumptions.

The checked path runs `Engine.analyzeSource`, `Parsing.parse`, `Model.analyze`, `Preprocessor.inactiveLines`, `Preprocessor.maskInactiveLines`, `Scanner.scan`, `SyntaxModel.normalize`, `NonCodeText.lines`, and `Rules.camelCaseVariableName`.

The last confirmed code sync is commit `5a78d17eff0985400505e3ebbc17a21eda24a30a`. Inspect changes since that commit, including uncommitted changes, to the following files before relying on the result:

- `src/MessFSharp/Parsing.fs` and `src/MessFSharp/Preprocessor.fs`.
- `src/MessFSharp/Scanner.fs`, `src/MessFSharp/NonCodeText.fs`, and `src/MessFSharp/SyntaxModel.fs`.
- `src/MessFSharp/Model.fs`, `src/MessFSharp/Engine.fs`, `src/MessFSharp/Domain.fs`, and `src/MessFSharp/Rules.fs`.
- `src/MessFSharp/MessFSharp.fsproj`, `src/MessFSharp/packages.lock.json`, `Directory.Build.props`, and `global.json`.
- This skill and its scripts.

For example, run `git diff 5a78d17eff0985400505e3ebbc17a21eda24a30a -- src/MessFSharp Directory.Build.props global.json .agents/skills/verify-conditional-compilation`. Confirm that the grammar, truth table, and selected rule still describe the intended code before moving the sync commit forward. If covered code changed without this review, use `/maintain-formal-verification-skill` to bring the check back in step.
