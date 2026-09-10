# Changelog

## 0.1.4

- Fix duplicate `KeyValuePair` keys in `Dictionary` constructors going undetected (#91).
- Fix record and list-pattern `let` bindings being misclassified as functions (#90).
- Fix active-pattern input references leaking outside the binding body (#89).
- Fix block comments affecting rule length checks (#88).
- Fix tuple-destructured `let` bindings being misclassified as functions (#87).
- Fix `EmptyCatchBlock` false positives when block comments precede the handler (#86).
- Fix duplicate normalized operator bindings (#85).
- Fix property accessors being counted as function parameters (#68) (#80).
- Fix `let!` bindings being classified as functions (#79).
- Fix nested functions leaking into outer body scopes (#66) (#78).
- Fix declarations being detected in multiline strings and comments (#65) (#77).
- Fix non-code text affecting `LackOfCohesion` references (#64) (#76).
- Fix `BooleanArgumentFlag` false positives on string literals and identifiers (#63) (#75).
- Fix multiline primary constructors not being detected (#62) (#74).
- Fix declarations in inactive `#if` branches being analyzed (#73).
- Fix member-local `let` bindings being reported as class fields.

## 0.1.3

- Fix `ElseExpression` false positives when string literals or comments contain `raise`, `failwith`, or `Environment.Exit` (#34).
- Fix `ExitExpression` missing F# `exit`, `Environment.Exit`, and identifier arguments (#33).
- Fix `StaticAccess` false positives on declarations, type annotations, and attributes (#31).
- Fix same-line attributes being ignored for suppressions and compiler bindings (#30).
- Fix compiler bindings duplicating member and property declarations (#29).
- Fix `--ignore-tests` skipping production files when an ancestor directory ends in `Tests` (#35).
- Fix mutually recursive `and` bindings being parsed as a single declaration (#36).
- Fix `EmptyCatchBlock` false positives on pattern-match unit clauses (#37).
- Fix `DuplicatedArrayKey` false positives on nested expressions and string literals (#38).
- Fix GitHub annotation message bodies encoding colons and commas (#39).
- Fix scanner not recognizing unicode, hex, and decimal character escape sequences (#40).
- Fix extended string interpolation (`$$`) causing unused-variable false positives (#41).
- Fix boolean member detection matching `true`/`false` in comments, strings, and bodies (#42).
- Fix `CouplingBetweenObjects` counting comments, string literals, own members, and built-in types (#43).

## 0.1.2

- Add production and development Dockerfiles (#26).
- Set runtime WORKDIR to /code and add default help CMD (#27).

## 0.1.1

- Fix issue where CLI arguments named "help" hijacked command parsing (#22).
- Fix case-insensitive property overrides replacing default rule properties in custom rulesets (#13).
- Fix double-backtick identifiers with spaces or keywords being scanned as tokens and reference counted (#14).
- Fix verbatim string scanning with trailing backslashes (#20).
- Fix interpolated string expressions being tokenized as code tokens (#21).
- Fix `DuplicatedArrayKey` false positives on tuple values in map/dictionary entries (#15).
- Fix `StaticAccess` false positives on `open` directives (#16).
- Fix `BooleanArgumentFlag` false positives on words containing "use" like `isUser` (#17).
- Fix `ElseExpression` flagging outer else blocks when only nested branches terminate (#18).
- Fix `SuppressMessage` on let-bindings being ignored due to attribute offset line duplication (#19).
- Fix `applyCompilerTypeShapes` failing to match interface declarations with preceding attributes (#23).
- Fix `ExcessivePublicCount` counting local function bindings as public module declarations.

## 0.1.0

- Initial standalone F# mess detector and .NET tool packaging.
