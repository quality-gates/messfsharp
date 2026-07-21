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
