# Changelog

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
