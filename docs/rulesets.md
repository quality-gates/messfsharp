# Rulesets

`messfsharp` keeps one stable rule identity across text, structured reports,
configuration, and CI annotations. Built-in names are case-insensitive.

| Ruleset | Rules |
| --- | --- |
| `codesize` | `CyclomaticComplexity`, `NPathComplexity`, `ExcessiveMethodLength`, `ExcessiveClassLength`, `ExcessiveParameterList`, `ExcessivePublicCount`, `TooManyFields`, `TooManyMethods`, `TooManyPublicMethods`, `ExcessiveClassComplexity` |
| `naming` | `ShortClassName`, `LongClassName`, `ShortVariable`, `LongVariable`, `ShortMethodName`, `ConstantNamingConventions`, `BooleanGetMethodName` |
| `unusedcode` | `UnusedPrivateField`, `UnusedLocalVariable`, `UnusedPrivateMethod`, `UnusedFormalParameter` |
| `cleancode` | `BooleanArgumentFlag`, `ElseExpression`, `StaticAccess`, `IfStatementAssignment`, `DuplicatedArrayKey` |
| `design` | `ExitExpression`, `GotoStatement`, `CountInLoopExpression`, `DevelopmentCodeFragment`, `EmptyCatchBlock`, `CouplingBetweenObjects`, `GlobalVariable`, `LackOfCohesionOfMethods` |
| `controversial` | `CamelCaseClassName`, `CamelCaseMethodName`, `CamelCasePropertyName`, `CamelCaseParameterName`, `CamelCaseVariableName` |
| `explicitness` | `ImplicitInput`, `ImplicitOutput`, `UnknownEffect` |
| `strictexplicitness` | `ImplicitClassInput`, `ImplicitClassOutput` |

The recommended `fsharp` ruleset composes the catalog and deliberately leaves
out `UnusedFormalParameter`, `ElseExpression`, `BooleanArgumentFlag`,
`StaticAccess`, `ShortVariable`, and `CountInLoopExpression`. It adds
`LongVariable` back with a maximum length of 35. The `opinionated` ruleset
contains the intentionally stricter checks omitted from that default:
`UnusedFormalParameter`, `ElseExpression`, `BooleanArgumentFlag`,
`StaticAccess`, `ShortVariable`, and `CountInLoopExpression`.

## Explicitness

The `explicitness` and `strictexplicitness` rulesets are opt-in and are not part
of `fsharp`. They follow the implicit input and output model from *Grokking
Simplicity*: a function's explicit inputs are its arguments and its explicit
output is its return value; anything else is implicit.

`ImplicitInput` reports a function or member that reads:

- a module-level or `static let mutable`, `ref` cell, or mutable collection
  (`ResizeArray`, `Dictionary`, `HashSet`, `StringBuilder`, and similar,
  either constructed directly or annotated with the type), whether named
  directly, qualified by its module (`State.count`), or through an `open`; or
- ambient input such as `DateTime.Now`, `Guid.NewGuid`, `Random.Shared`,
  `Environment.GetEnvironmentVariable`, `Console.ReadLine`, `stdin`, and
  `File`/`Directory` reads.

Immutable module values are not inputs, because they cannot change between
calls. Module-level arrays are also not treated as inputs when read, because
they are usually lookup tables; writes to them are still outputs.

`ImplicitOutput` reports a function or member that:

- assigns (`<-`, `:=`, `incr`, `decr`, indexer set, `Array.set`) to anything
  that is not one of its own locals;
- mutates an argument, either by assignment or by calling `Add`, `Remove`,
  `Clear`, `Append`, and similar on a parameter annotated as a mutable
  collection; or
- performs ambient output such as `printfn`, `eprintfn`, `Console.WriteLine`,
  `File.WriteAllText`, `Directory.CreateDirectory`, or setting
  `Environment.CurrentDirectory`.

`UnknownEffect` reports a call whose effects cannot be established in the
supported analyzed subset. It is part of the `explicitness` ruleset, so
unresolved calls do not masquerade as calculations. Known and unknown effects
can coexist: a function that writes to stdout and also invokes an injected
callback reports both the ambient output and the unknown callback effect.

The callable summary used by these rules records parameter-relative writes,
ambient reads and writes, unknown calls, and returned callable owners. Summaries
are propagated through same-file calls to a finite fixed point. Mutating a
caller-owned argument remains an output, while mutating storage freshly allocated
inside the caller is treated as local. `Array.copy` is modeled as a shallow copy:
the copied array itself is fresh, but reachable elements inside it are still
caller-owned. Conditional storage facts join their branches, so a value that may
be a caller-owned array remains caller-owned for writes.

The trusted calculation subset is deliberately narrow: exact built-in numeric
operations, plain built-in records and containers, local functions resolved in
the same file, known standard pure operations such as `Array.copy` and
`List.map`, and unmodified standard-library union cases such as `Some` and
`None`. User-defined type constructors declared in the same file are treated as
allocation, not ambient effects. Type annotations alone do not prove that an
arbitrary runtime value is pure.

`strictexplicitness` adds the same checks for a type's own data. It is
intended to be combined with `explicitness`. `ImplicitClassInput` reports
members that read `let` fields, primary constructor arguments, or `this.X`.
`ImplicitClassOutput` reports members that assign to those fields or call a
mutating method on a mutable collection field or `member val`. Calling or
passing another method of the same type through the self identifier is not
reported.

Each owner reports a given name once per direction, at its first occurrence.
Nested functions and lambdas belong to their enclosing module-level function
or member. Locals, including local functions and parameters of nested lambdas,
shadow outer names only within their own scope. Module values resolve from the
innermost enclosing module outwards, then through `open` declarations.
The analysis is still intentionally bounded. It does not prove arbitrary-runtime
purity, inspect dynamic dispatch, model generators or lazy iteration, execute
deferred callbacks at creation time, model async scheduling, or infer effects
for opaque external calls. Callback creation is separate from invocation:
returning `fun () -> callback ()` attributes the callback invocation to the
returned lambda, while immediately calling `callback ()` attributes the unknown
effect to the current owner. If a receiver or callback effect cannot be
established under this policy, the analyzer keeps uncertainty rather than
certifying the call as pure. Exceptions and `exit` are not treated as outputs.
Module-level values that are not functions, such as `let job = async { ... }`,
have no owner and are not checked.

The exploratory testing reports ([2026-09-23](exploratory-testing/2026-09-23-explicitness.md),
[2026-09-26](exploratory-testing/2026-09-26-messfsharp.md))
record how these rules were exercised, bugs found, and open questions.

```console
messfsharp src text explicitness --ignore-tests
messfsharp src text explicitness,strictexplicitness --ignore-tests
```

Default thresholds are cyclomatic complexity 10, NPath complexity 200, method
length 100 lines, type length 1000 lines, parameter count 10, public count 45,
fields 15, methods 25, public methods 10, aggregate type complexity 50,
type names 3–40 characters, variable names 3–20 characters, function/member
names at least 3 characters, coupling 13, and LCOM4 1. Rule properties can
override these values in custom XML rulesets.

The supported rule properties are:

| Rules | Properties and defaults |
| --- | --- |
| `CyclomaticComplexity` | `maximum=10`, `reportLevel=10` |
| `NPathComplexity` | `maximum=200`, `reportLevel=200` |
| `ExcessiveMethodLength` | `minimum=100`, `ignore-whitespace=true` |
| `ExcessiveClassLength` | `minimum=1000`, `ignore-whitespace=true` |
| `ExcessiveParameterList` | `maximum=10`, `reportLevel=10` |
| `ExcessivePublicCount` | `maximum=45`, `reportLevel=45` |
| `TooManyFields` | `maxfields=15`, `reportLevel=15` |
| `TooManyMethods` | `maxmethods=25`, `reportLevel=25` |
| `TooManyPublicMethods` | `maxmethods=10`, `reportLevel=10` |
| `ExcessiveClassComplexity` | `maximum=50`, `reportLevel=50` |
| `ShortClassName` | `minimum=3` |
| `LongClassName` | `maximum=40` |
| `ShortVariable` | `minimum=3`, `ignorepattern=^(x|xs|f|g|_|_.*)$`; optional `exceptions`, `subtract-prefixes`, `subtract-suffixes` |
| `LongVariable` | `maximum=20`, `ignorepattern=^(x|xs|f|g|_|_.*)$`; optional `exceptions`, `subtract-prefixes`, `subtract-suffixes` |
| `ShortMethodName` | `minimum=3` |
| `ConstantNamingConventions` | `convention=PascalCase` |
| `BooleanGetMethodName` | `checkParameterizedMethods=true` |
| `CouplingBetweenObjects` | `maximum=13` |
| `LackOfCohesionOfMethods` | `minimum=1` |
| `GlobalVariable` | `report-immutable=false` |
| `DevelopmentCodeFragment` | `unwanted-functions=TODO,FIXME,HACK,Debug.Assert` |

Rules without a row above have no configurable properties. Property names are
case-insensitive and values are interpreted according to the rule; unknown
properties are retained in the selection for forward-compatible custom rules.

`reportLevel` is the compatibility alias shipped alongside the primary numeric
threshold shown in the table. `ignore-whitespace` chooses whether blank and
comment-only lines count toward length. `ignorepattern` is a regular expression;
`exceptions` is a comma- or semicolon-separated exact-name list; and
`subtract-prefixes` / `subtract-suffixes` remove configured affixes before a
length check. `convention` accepts PascalCase, camelCase, or uppercase.
`checkParameterizedMethods` includes boolean members with parameters.
`unwanted-functions` is a comma-separated conservative marker/call list, and
`report-immutable=true` broadens `GlobalVariable` beyond observed mutation.

The [2026-10-03 exploratory testing report](exploratory-testing/2026-10-03-messfsharp.md)
records how custom rulesets, suppression, and CI output were exercised on real
projects, and the bugs found.
