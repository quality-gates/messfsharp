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

The recommended `fsharp` ruleset composes the catalog and deliberately leaves
out `UnusedFormalParameter`, `ElseExpression`, `BooleanArgumentFlag`,
`StaticAccess`, `ShortVariable`, and `CountInLoopExpression`. It adds
`LongVariable` back with a maximum length of 35. The `opinionated` ruleset
contains the intentionally stricter checks omitted from that default:
`UnusedFormalParameter`, `ElseExpression`, `BooleanArgumentFlag`,
`StaticAccess`, `ShortVariable`, and `CountInLoopExpression`.

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
