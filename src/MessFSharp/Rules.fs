namespace MessFSharp

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessivePublicCount")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "GlobalVariable")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "DevelopmentCodeFragment")>]
module Rules =
    let private ruleUri (name: string) =
        Some(sprintf "https://github.com/quality-gates/messfsharp#%s" (name.ToLowerInvariant()))

    let private property (selection: RuleSelection) (name: string) (fallback: int) =
        let configured =
            selection.Properties
            |> Seq.tryPick (fun item ->
                if String.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase) then
                    Some item.Value
                else
                    None)

        match configured with
        | Some value ->
            match Int32.TryParse(value: string) with
            | true, parsed -> parsed
            | _ -> fallback
        | None -> fallback

    let private propertyText (selection: RuleSelection) (name: string) (fallback: string) =
        selection.Properties
        |> Seq.tryPick (fun item ->
            if String.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase) then
                Some item.Value
            else
                None)
        |> Option.defaultValue fallback

    let private context (file: AnalyzedFile) (declaration: Declaration option) =
        let line =
            declaration
            |> Option.map (fun item -> item.Location.StartLine)
            |> Option.defaultValue 1

        let enclosing (kind: DeclarationKind) =
            file.Declarations
            |> List.filter (fun item ->
                item.Kind = kind && item.Location.StartLine <= line && item.ScopeEndLine >= line)
            |> List.sortByDescending (fun item -> item.Location.StartLine)
            |> List.tryHead
            |> Option.map (fun item -> item.Name)

        { Namespace = enclosing Namespace
          Module = enclosing Module
          Type = enclosing Type
          Member =
            declaration
            |> Option.bind (fun item ->
                if item.Kind = Member || item.Kind = Property then
                    Some item.Name
                else
                    None) }

    let private violation
        (file: AnalyzedFile)
        (selection: RuleSelection)
        (declaration: Declaration option)
        (line: int)
        (description: string)
        =
        let location =
            match declaration with
            | Some item -> item.Location
            | None ->
                { File = file.Source.FullPath
                  StartLine = line
                  StartColumn = 1
                  EndLine = line
                  EndColumn =
                    max
                        1
                        (file.Source.Lines
                         |> Array.tryItem (line - 1)
                         |> Option.map String.length
                         |> Option.defaultValue 1) }

        { Location = location
          RuleName = selection.Name
          RulesetName = selection.RulesetName
          Priority = selection.Priority
          Description = description
          Context = context file declaration
          HelpUri = ruleUri selection.Name }

    let private allDeclarations (file: AnalyzedFile) (predicate: Declaration -> bool) =
        file.Declarations |> List.filter predicate

    let private metric (map: Map<string * int, int>) (declaration: Declaration) =
        Map.tryFind (declaration.Name, declaration.Location.StartLine) map
        |> Option.defaultValue 0

    let private referenceCount (file: AnalyzedFile) (declaration: Declaration) =
        Map.tryFind (declaration.Name, declaration.Location.StartLine) file.ReferenceCountsByDeclaration
        |> Option.defaultValue (Map.tryFind declaration.Name file.ReferenceCounts |> Option.defaultValue 0)

    let private hasToken (file: AnalyzedFile) (token: string) =
        file.Tokens |> Array.exists (fun item -> item.Text = token)

    let private bodyHas (declaration: Declaration) (text: string) =
        declaration.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0

    let private meaningfulLineCount (declaration: Declaration) =
        declaration.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
        |> Array.filter (fun line ->
            let trimmed = line.Trim()

            not (String.IsNullOrWhiteSpace trimmed)
            && not (trimmed.StartsWith("//", StringComparison.Ordinal)))
        |> Array.length

    let private configuredLineCount (selection: RuleSelection) (declaration: Declaration) rawCount =
        let ignoreWhitespace =
            propertyText selection "ignore-whitespace" "true"
            |> fun value -> value.Equals("true", StringComparison.OrdinalIgnoreCase)

        if ignoreWhitespace then
            meaningfulLineCount declaration
        else
            rawCount

    let private isOperatorName (name: string) =
        name
        |> Seq.exists (fun character -> "!%&*+-./<=>?@^|~:".IndexOf(character) >= 0)

    let private isNameLike (name: string) =
        not (String.IsNullOrWhiteSpace name)
        && not (isOperatorName name)
        && not (name.StartsWith("'", StringComparison.Ordinal))
        && not (name.StartsWith("get_", StringComparison.Ordinal))

    let private startsWithUpper (name: string) =
        isNameLike name && Char.IsUpper(name[0])

    let private startsWithLower (name: string) =
        isNameLike name && (Char.IsLower(name[0]) || name[0] = '_')

    let private defsFor
        (predicate: Declaration -> bool)
        (file: AnalyzedFile)
        (selection: RuleSelection)
        (description: Declaration -> string option)
        =
        allDeclarations file predicate
        |> List.choose (fun declaration ->
            if description declaration |> Option.isSome then
                Some(
                    violation
                        file
                        selection
                        (Some declaration)
                        declaration.Location.StartLine
                        (description declaration |> Option.get)
                )
            else
                None)

    let cyclomaticComplexity =
        { Name = "CyclomaticComplexity"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "10"; "reportLevel", "10" ]
          Description = "Reports functions and members whose cyclomatic complexity exceeds the configured maximum."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 10

                defsFor
                    (fun declaration -> declaration.Kind = Function || declaration.Kind = Member)
                    file
                    selection
                    (fun declaration ->
                        let value = metric file.ComplexityByDeclaration declaration

                        if value > maximum then
                            Some(sprintf "Cyclomatic complexity %d exceeds maximum %d." value maximum)
                        else
                            None) }

    let nPathComplexity =
        { Name = "NPathComplexity"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "200"; "reportLevel", "200" ]
          Description = "Reports functions and members with too many possible execution paths."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 200

                defsFor
                    (fun declaration -> declaration.Kind = Function || declaration.Kind = Member)
                    file
                    selection
                    (fun declaration ->
                        let value = metric file.NPathByDeclaration declaration

                        if value > maximum then
                            Some(sprintf "NPath complexity %d exceeds maximum %d." value maximum)
                        else
                            None) }

    let excessiveMethodLength =
        { Name = "ExcessiveMethodLength"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "minimum", "100"; "ignore-whitespace", "true" ]
          Description = "Reports functions and members longer than the configured line threshold."
          Check =
            fun file selection ->
                let minimum = property selection "minimum" 100

                defsFor
                    (fun declaration -> declaration.Kind = Function || declaration.Kind = Member)
                    file
                    selection
                    (fun declaration ->
                        let value =
                            configuredLineCount selection declaration (metric file.LineCountByDeclaration declaration)

                        if value > minimum then
                            Some(sprintf "Method length %d exceeds maximum %d lines." value minimum)
                        else
                            None) }

    let excessiveClassLength =
        { Name = "ExcessiveClassLength"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "minimum", "1000"; "ignore-whitespace", "true" ]
          Description = "Reports type declarations longer than the configured line threshold."
          Check =
            fun file selection ->
                let minimum = property selection "minimum" 1000

                defsFor
                    (fun declaration -> declaration.Kind = Type && declaration.IsClassLike)
                    file
                    selection
                    (fun declaration ->
                        let value =
                            configuredLineCount selection declaration (metric file.LineCountByDeclaration declaration)

                        if value > minimum then
                            Some(sprintf "Type length %d exceeds maximum %d lines." value minimum)
                        else
                            None) }

    let excessiveParameterList =
        { Name = "ExcessiveParameterList"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "10"; "reportLevel", "10" ]
          Description = "Reports functions and members with too many logical parameters."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 10

                defsFor
                    (fun declaration -> declaration.Kind = Function || declaration.Kind = Member)
                    file
                    selection
                    (fun declaration ->
                        if declaration.ParameterCount > maximum then
                            Some(sprintf "Parameter count %d exceeds maximum %d." declaration.ParameterCount maximum)
                        else
                            None) }

    let excessivePublicCount =
        { Name = "ExcessivePublicCount"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "45"; "reportLevel", "45" ]
          Description = "Reports type-like units with too many public declarations."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 45

                file.Declarations
                |> List.filter (fun declaration -> declaration.Kind = Type || declaration.Kind = Module)
                |> List.choose (fun declaration ->
                    let count =
                        file.Declarations
                        |> List.filter (fun child ->
                            child.Location.StartLine >= declaration.Location.StartLine
                            && child.Location.StartLine <= declaration.ScopeEndLine
                            && child.IsPublic
                            && child.Kind <> Parameter)
                        |> List.length

                    if count > maximum then
                        Some(
                            violation
                                file
                                selection
                                (Some declaration)
                                declaration.Location.StartLine
                                (sprintf "Public declaration count %d exceeds maximum %d." count maximum)
                        )
                    else
                        None) }

    let tooManyFields =
        { Name = "TooManyFields"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maxfields", "15"; "reportLevel", "15" ]
          Description = "Reports data or class types with too many declared fields."
          Check =
            fun file selection ->
                let maximum = property selection "maxfields" 15

                file.Declarations
                |> List.filter (fun declaration -> declaration.Kind = Type)
                |> List.choose (fun declaration ->
                    let count =
                        file.Declarations
                        |> List.filter (fun child -> child.Kind = Field && child.Parent = Some declaration.Name)
                        |> List.length

                    if count > maximum then
                        Some(
                            violation
                                file
                                selection
                                (Some declaration)
                                declaration.Location.StartLine
                                (sprintf "Field count %d exceeds maximum %d." count maximum)
                        )
                    else
                        None) }

    let tooManyMethods =
        { Name = "TooManyMethods"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maxmethods", "25"; "reportLevel", "25" ]
          Description = "Reports type-like units with too many methods or functions."
          Check =
            fun file selection ->
                let maximum = property selection "maxmethods" 25

                file.Declarations
                |> List.filter (fun declaration -> declaration.Kind = Type || declaration.Kind = Module)
                |> List.choose (fun declaration ->
                    let count =
                        file.Declarations
                        |> List.filter (fun child ->
                            (child.Kind = Member || child.Kind = Function)
                            && child.Parent = Some declaration.Name)
                        |> List.length

                    if count > maximum then
                        Some(
                            violation
                                file
                                selection
                                (Some declaration)
                                declaration.Location.StartLine
                                (sprintf "Method count %d exceeds maximum %d." count maximum)
                        )
                    else
                        None) }

    let tooManyPublicMethods =
        { Name = "TooManyPublicMethods"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maxmethods", "10"; "reportLevel", "10" ]
          Description = "Reports type-like units with too many public methods."
          Check =
            fun file selection ->
                let maximum = property selection "maxmethods" 10

                file.Declarations
                |> List.filter (fun declaration -> declaration.Kind = Type || declaration.Kind = Module)
                |> List.choose (fun declaration ->
                    let count =
                        file.Declarations
                        |> List.filter (fun child ->
                            (child.Kind = Member || child.Kind = Function)
                            && child.Parent = Some declaration.Name
                            && child.IsPublic)
                        |> List.length

                    if count > maximum then
                        Some(
                            violation
                                file
                                selection
                                (Some declaration)
                                declaration.Location.StartLine
                                (sprintf "Public method count %d exceeds maximum %d." count maximum)
                        )
                    else
                        None) }

    let excessiveClassComplexity =
        { Name = "ExcessiveClassComplexity"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "50"; "reportLevel", "50" ]
          Description = "Reports type-like units whose aggregate complexity exceeds the configured maximum."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 50

                file.Declarations
                |> List.filter (fun declaration -> declaration.Kind = Type && declaration.IsClassLike)
                |> List.choose (fun declaration ->
                    let value =
                        file.Declarations
                        |> List.filter (fun child ->
                            child.Parent = Some declaration.Name
                            && (child.Kind = Member || child.Kind = Function))
                        |> List.sumBy (metric file.ComplexityByDeclaration)

                    if value > maximum then
                        Some(
                            violation
                                file
                                selection
                                (Some declaration)
                                declaration.Location.StartLine
                                (sprintf "Aggregate type complexity %d exceeds maximum %d." value maximum)
                        )
                    else
                        None) }

    let shortClassName =
        { Name = "ShortClassName"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "minimum", "3" ]
          Description = "Reports type names shorter than the configured minimum."
          Check =
            fun file selection ->
                let minimum = property selection "minimum" 3

                defsFor
                    (fun declaration -> declaration.Kind = Type && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if declaration.Name.Length < minimum then
                            Some(sprintf "Type name '%s' is shorter than minimum length %d." declaration.Name minimum)
                        else
                            None) }

    let longClassName =
        { Name = "LongClassName"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "40" ]
          Description = "Reports type names longer than the configured maximum."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 40

                defsFor
                    (fun declaration -> declaration.Kind = Type && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if declaration.Name.Length > maximum then
                            Some(sprintf "Type name '%s' exceeds maximum length %d." declaration.Name maximum)
                        else
                            None) }

    let shortVariable =
        { Name = "ShortVariable"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "minimum", "3"; "ignorepattern", "^(x|xs|f|g|_|_.*)$" ]
          Description = "Reports variable and parameter names shorter than the configured minimum."
          Check =
            fun file selection ->
                let minimum = property selection "minimum" 3
                let ignorePattern = propertyText selection "ignorepattern" "^(x|xs|f|g|_|_.*)$"
                let ignored (name: string) = Regex.IsMatch(name, ignorePattern)

                defsFor
                    (fun declaration ->
                        (declaration.Kind = Value
                         || declaration.Kind = Parameter
                         || declaration.Kind = Field)
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if declaration.Name.Length < minimum && not (ignored declaration.Name) then
                            Some(
                                sprintf
                                    "Variable name '%s' is shorter than minimum length %d."
                                    declaration.Name
                                    minimum
                            )
                        else
                            None) }

    let longVariable =
        { Name = "LongVariable"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "20"; "ignorepattern", "^(x|xs|f|g|_|_.*)$" ]
          Description = "Reports variable and parameter names longer than the configured maximum."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 20
                let ignorePattern = propertyText selection "ignorepattern" "^(x|xs|f|g|_|_.*)$"
                let ignored (name: string) = Regex.IsMatch(name, ignorePattern)

                defsFor
                    (fun declaration ->
                        (declaration.Kind = Value
                         || declaration.Kind = Parameter
                         || declaration.Kind = Field)
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if declaration.Name.Length > maximum && not (ignored declaration.Name) then
                            Some(sprintf "Variable name '%s' exceeds maximum length %d." declaration.Name maximum)
                        else
                            None) }

    let shortMethodName =
        { Name = "ShortMethodName"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "minimum", "3" ]
          Description = "Reports function and member names shorter than the configured minimum."
          Check =
            fun file selection ->
                let minimum = property selection "minimum" 3

                defsFor
                    (fun declaration ->
                        (declaration.Kind = Function
                         || declaration.Kind = Member
                         || declaration.Kind = Property)
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if
                            declaration.Name.Length < minimum
                            && not ([ "x"; "xs"; "f"; "g" ] |> List.contains declaration.Name)
                        then
                            Some(
                                sprintf "Method name '%s' is shorter than minimum length %d." declaration.Name minimum
                            )
                        else
                            None) }

    let constantNamingConventions =
        { Name = "ConstantNamingConventions"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "convention", "PascalCase" ]
          Description = "Reports literal declarations that do not follow the configured F# constant convention."
          Check =
            fun file selection ->
                let convention = propertyText selection "convention" "PascalCase"

                let followsConvention (name: string) =
                    match convention.Trim().ToLowerInvariant() with
                    | "uppercase"
                    | "upper" -> name = name.ToUpperInvariant()
                    | "camelcase"
                    | "camel" -> startsWithLower name
                    | _ -> startsWithUpper name

                defsFor
                    (fun declaration -> declaration.IsLiteral && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if not (followsConvention declaration.Name) then
                            Some(
                                sprintf
                                    "Literal constant '%s' does not follow %s convention."
                                    declaration.Name
                                    convention
                            )
                        else
                            None) }

    let booleanGetMethodName =
        { Name = "BooleanGetMethodName"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "checkParameterizedMethods", "true" ]
          Description = "Reports boolean members named with a get prefix instead of an is/has prefix."
          Check =
            fun file selection ->
                let checkParameterized =
                    propertyText selection "checkParameterizedMethods" "true"
                    |> fun value -> value.Equals("true", StringComparison.OrdinalIgnoreCase)

                defsFor
                    (fun declaration ->
                        (declaration.Kind = Member || declaration.Kind = Property)
                        && declaration.Name.StartsWith("get", StringComparison.OrdinalIgnoreCase)
                        && declaration.IsBoolean
                        && (checkParameterized || declaration.ParameterCount = 0))
                    file
                    selection
                    (fun declaration ->
                        Some(sprintf "Boolean member '%s' should use an is/has name instead of get." declaration.Name)) }

    let unusedPrivateField =
        { Name = "UnusedPrivateField"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports private fields that have no reference in their source scope."
          Check =
            fun file selection ->
                defsFor
                    (fun declaration -> declaration.Kind = Field && declaration.IsPrivate && not declaration.IsIgnored)
                    file
                    selection
                    (fun declaration ->
                        if referenceCount file declaration <= 1 then
                            Some(sprintf "Private field '%s' is never used." declaration.Name)
                        else
                            None) }

    let unusedLocalVariable =
        { Name = "UnusedLocalVariable"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports local values and functions that are never referenced in their lexical scope."
          Check =
            fun file selection ->
                defsFor
                    (fun declaration ->
                        ((declaration.Kind = Value && not declaration.IsModuleLevel)
                         || (declaration.Kind = Function && not declaration.IsModuleLevel))
                        && not declaration.IsIgnored
                        && not declaration.IsCompilerGenerated)
                    file
                    selection
                    (fun declaration ->
                        if referenceCount file declaration <= 1 then
                            Some(sprintf "Local binding '%s' is never used." declaration.Name)
                        else
                            None) }

    let unusedPrivateMethod =
        { Name = "UnusedPrivateMethod"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports private members that are never referenced."
          Check =
            fun file selection ->
                defsFor
                    (fun declaration ->
                        (declaration.Kind = Member || declaration.Kind = Property)
                        && declaration.IsPrivate
                        && not declaration.IsIgnored)
                    file
                    selection
                    (fun declaration ->
                        if referenceCount file declaration <= 1 then
                            Some(sprintf "Private method '%s' is never used." declaration.Name)
                        else
                            None) }

    let unusedFormalParameter =
        { Name = "UnusedFormalParameter"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports formal parameters that are not referenced in the complete function scope."
          Check =
            fun file selection ->
                defsFor
                    (fun declaration -> declaration.Kind = Parameter && not declaration.IsIgnored)
                    file
                    selection
                    (fun declaration ->
                        if referenceCount file declaration <= 1 then
                            Some(sprintf "Formal parameter '%s' is never used." declaration.Name)
                        else
                            None) }

    let booleanArgumentFlag =
        { Name = "BooleanArgumentFlag"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports boolean parameters used as behaviour flags."
          Check =
            fun file selection ->
                defsFor
                    (fun declaration ->
                        declaration.Kind = Parameter
                        && declaration.IsBoolean
                        && (declaration.Name.IndexOf("flag", StringComparison.OrdinalIgnoreCase) >= 0
                            || declaration.Name.IndexOf("enable", StringComparison.OrdinalIgnoreCase) >= 0
                            || declaration.Name.IndexOf("use", StringComparison.OrdinalIgnoreCase) >= 0))
                    file
                    selection
                    (fun declaration ->
                        Some(sprintf "Boolean parameter '%s' controls multiple behaviours." declaration.Name)) }

    let elseExpression =
        { Name = "ElseExpression"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports only flattenable else branches after unconditional termination."
          Check =
            fun file selection ->
                file.Source.Lines
                |> Array.mapi (fun index line -> index + 1, line)
                |> Array.choose (fun (lineNumber, line) ->
                    if
                        line.IndexOf("else", StringComparison.OrdinalIgnoreCase) >= 0
                        && (line.IndexOf("failwith", StringComparison.OrdinalIgnoreCase) >= 0
                            || line.IndexOf("raise", StringComparison.OrdinalIgnoreCase) >= 0
                            || line.IndexOf("Environment.Exit", StringComparison.Ordinal) >= 0)
                    then
                        Some(
                            violation
                                file
                                selection
                                None
                                lineNumber
                                "An else branch follows an unconditional terminating expression and can be flattened."
                        )
                    else
                        None)
                |> Array.toList }

    let staticAccess =
        { Name = "StaticAccess"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports explicit static .NET access when this stricter policy is selected."
          Check =
            fun file selection ->
                file.Source.Lines
                |> Array.mapi (fun index line -> index + 1, line)
                |> Array.choose (fun (lineNumber, line) ->
                    if Regex.IsMatch(line, "\\b(System|Microsoft)\\.[A-Z][A-Za-z0-9_]*\\.") then
                        Some(
                            violation
                                file
                                selection
                                None
                                lineNumber
                                "Explicit static access is enabled by an opinionated policy."
                        )
                    else
                        None)
                |> Array.toList }

    let ifStatementAssignment =
        { Name = "IfStatementAssignment"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reserved compatibility rule for assignment-in-if constructs, which F# does not use."
          Check = fun _ _ -> [] }

    let duplicatedArrayKey =
        { Name = "DuplicatedArrayKey"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports duplicate statically knowable keys in map or dictionary construction."
          Check =
            fun file selection ->
                let keyPattern =
                    Regex("\\(\\s*(\"[^\"]*\"|'[^']*'|[0-9]+)\\s*,", RegexOptions.Compiled)

                file.Source.Lines
                |> Array.mapi (fun index line -> index + 1, line)
                |> Array.choose (fun (lineNumber, line) ->
                    if
                        line.IndexOf("Map.ofList", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("dict", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("Dictionary", StringComparison.OrdinalIgnoreCase) >= 0
                    then
                        let keys =
                            keyPattern.Matches(line)
                            |> Seq.cast<Match>
                            |> Seq.map (fun item -> item.Groups[1].Value)
                            |> Seq.toList

                        if keys.Length <> (keys |> List.distinct |> List.length) then
                            Some(
                                violation
                                    file
                                    selection
                                    None
                                    lineNumber
                                    "A map or dictionary construction contains a duplicate key."
                            )
                        else
                            None
                    else
                        None)
                |> Array.toList }

    let exitExpression =
        { Name = "ExitExpression"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports process-exit expressions selected by the design policy."
          Check =
            fun file selection ->
                file.Source.Lines
                |> Array.mapi (fun index line -> index + 1, line)
                |> Array.choose (fun (lineNumber, line) ->
                    if Regex.IsMatch(line, "\\b(exit|Environment\\.Exit)\\s*\\(") then
                        Some(
                            violation
                                file
                                selection
                                None
                                lineNumber
                                "Process exit is embedded in an analyzable expression."
                        )
                    else
                        None)
                |> Array.toList }

    let gotoStatement =
        { Name = "GotoStatement"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reserved compatibility rule for goto statements, which F# does not provide."
          Check = fun _ _ -> [] }

    let countInLoopExpression =
        { Name = "CountInLoopExpression"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports collection-count expressions embedded in loops."
          Check =
            fun file selection ->
                file.Source.Lines
                |> Array.mapi (fun index line -> index + 1, line)
                |> Array.choose (fun (lineNumber, line) ->
                    if
                        Regex.IsMatch(line, "\\b(for|while)\\b")
                        && Regex.IsMatch(line, "\\.(Length|Count)\\b")
                    then
                        Some(violation file selection None lineNumber "Collection count is evaluated inside a loop.")
                    else
                        None)
                |> Array.toList }

    let developmentCodeFragment =
        { Name = "DevelopmentCodeFragment"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "unwanted-functions", "TODO,FIXME,HACK,Debug.Assert" ]
          Description = "Reports explicit development markers and debugging fragments."
          Check =
            fun file selection ->
                let unwanted =
                    propertyText selection "unwanted-functions" "TODO,FIXME,HACK,Debug.Assert"

                let patterns =
                    unwanted.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun item -> item.Trim())

                file.Source.Lines
                |> Array.mapi (fun index line -> index + 1, line)
                |> Array.choose (fun (lineNumber, line) ->
                    if
                        patterns
                        |> Array.exists (fun pattern -> line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    then
                        Some(
                            violation
                                file
                                selection
                                None
                                lineNumber
                                "Development-only marker found in production source."
                        )
                    else
                        None)
                |> Array.toList }

    let emptyCatchBlock =
        { Name = "EmptyCatchBlock"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports exception handlers whose branch does no meaningful work."
          Check =
            fun file selection ->
                file.Source.Lines
                |> Array.mapi (fun index line -> index + 1, line)
                |> Array.choose (fun (lineNumber, line) ->
                    let nearby =
                        file.Source.Lines
                        |> Array.skip (max 0 (lineNumber - 6))
                        |> Array.take (min 6 lineNumber)
                        |> String.concat "\n"

                    if
                        Regex.IsMatch(line, "\\|.*->\\s*\\(\\s*\\)\\s*$")
                        && nearby.IndexOf("try", StringComparison.OrdinalIgnoreCase) >= 0
                    then
                        Some(violation file selection None lineNumber "Exception handler is empty.")
                    else
                        None)
                |> Array.toList }

    let couplingBetweenObjects =
        { Name = "CouplingBetweenObjects"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "maximum", "13" ]
          Description = "Reports type declarations coupled to too many distinct external names."
          Check =
            fun file selection ->
                let maximum = property selection "maximum" 13

                file.Declarations
                |> List.filter (fun declaration -> declaration.Kind = Type && declaration.IsClassLike)
                |> List.choose (fun declaration ->
                    let names =
                        Regex.Matches(declaration.Text, "\\b[A-Z][A-Za-z0-9_']*\\b")
                        |> Seq.cast<Match>
                        |> Seq.map (fun item -> item.Value)
                        |> Seq.filter (fun name -> name <> declaration.Name)
                        |> Seq.distinct
                        |> Seq.length

                    if names > maximum then
                        Some(
                            violation
                                file
                                selection
                                (Some declaration)
                                declaration.Location.StartLine
                                (sprintf "Coupling count %d exceeds maximum %d." names maximum)
                        )
                    else
                        None) }

    let globalVariable =
        { Name = "GlobalVariable"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "report-immutable", "false" ]
          Description = "Reports mutable module values and static fields when mutation is observed."
          Check =
            fun file selection ->
                let reportImmutable =
                    propertyText selection "report-immutable" "false"
                    |> fun value -> value.Equals("true", StringComparison.OrdinalIgnoreCase)

                defsFor
                    (fun declaration ->
                        (declaration.Kind = Value && declaration.IsModuleLevel)
                        || (declaration.Kind = Field && declaration.IsStatic))
                    file
                    selection
                    (fun declaration ->
                        let mutated =
                            file.MutatedNames.Contains(declaration.Name)
                            || Regex.IsMatch(
                                file.Source.Text,
                                sprintf "\\b%s\\b\\s*(?::=|\\.Value\\s*<-)" (Regex.Escape declaration.Name)
                            )

                        if (mutated || reportImmutable) && not (declaration.IsIgnored) then
                            Some(
                                sprintf
                                    "Module-level shared value '%s' is mutable or globally visible."
                                    declaration.Name
                            )
                        else
                            None) }

    let lackOfCohesionOfMethods =
        { Name = "LackOfCohesionOfMethods"
          DefaultPriority = 3
          DefaultProperties = Map.ofList [ "minimum", "1" ]
          Description = "Reports stateful class-like types whose methods form disconnected cohesion groups."
          Check =
            fun file selection ->
                file.Declarations
                |> List.filter (fun declaration ->
                    declaration.Kind = Type
                    && declaration.IsClassLike
                    && not declaration.IsRecord
                    && not declaration.IsUnion)
                |> List.choose (fun typeDeclaration ->
                    let methods: Declaration list =
                        file.Declarations
                        |> List.filter (fun declaration ->
                            (declaration.Kind = Member || declaration.Kind = Function)
                            && declaration.Parent = Some typeDeclaration.Name)

                    let fields: Declaration list =
                        file.Declarations
                        |> List.filter (fun declaration ->
                            declaration.Kind = Field && declaration.Parent = Some typeDeclaration.Name)

                    if methods.Length > 1 && not (List.isEmpty fields) then
                        let uses (methodDeclaration: Declaration) (field: Declaration) =
                            bodyHas methodDeclaration field.Name

                        let mutable groups = 0
                        let mutable unvisited = methods

                        while not (List.isEmpty unvisited) do
                            groups <- groups + 1
                            let seed = List.head unvisited
                            let mutable cohesionGroup: Declaration list = [ seed ]
                            let mutable changed = true

                            while changed do
                                changed <- false

                                for (candidate: Declaration) in unvisited do
                                    if
                                        not (
                                            List.exists
                                                (fun (item: Declaration) ->
                                                    item.Name = candidate.Name
                                                    && item.Location.StartLine = candidate.Location.StartLine)
                                                cohesionGroup
                                        )
                                        && (cohesionGroup
                                            |> List.exists (fun (item: Declaration) ->
                                                fields
                                                |> List.exists (fun (field: Declaration) ->
                                                    uses item field && uses candidate field)))
                                    then
                                        cohesionGroup <- candidate :: cohesionGroup
                                        changed <- true

                            unvisited <-
                                unvisited
                                |> List.filter (fun (item: Declaration) ->
                                    not (
                                        List.exists
                                            (fun (memberItem: Declaration) ->
                                                memberItem.Name = item.Name
                                                && memberItem.Location.StartLine = item.Location.StartLine)
                                            cohesionGroup
                                    ))

                        if groups > property selection "minimum" 1 then
                            Some(
                                violation
                                    file
                                    selection
                                    (Some typeDeclaration)
                                    typeDeclaration.Location.StartLine
                                    (sprintf "Type methods form %d cohesion groups." groups)
                            )
                        else
                            None
                    else
                        None) }

    let private pascalCaseRule name kind predicate description =
        { Name = name
          DefaultPriority = 4
          DefaultProperties = Map.empty
          Description = description
          Check =
            fun file selection ->
                defsFor predicate file selection (fun declaration ->
                    if not (declaration.IsIgnored) && startsWithUpper declaration.Name then
                        None
                    elif declaration.IsIgnored then
                        None
                    else
                        Some(sprintf "%s '%s' should use PascalCase." kind declaration.Name)) }

    let camelCaseClassName =
        pascalCaseRule
            "CamelCaseClassName"
            "Type name"
            (fun declaration -> declaration.Kind = Type && isNameLike declaration.Name)
            "Reports type names that do not use F# PascalCase."

    let camelCaseMethodName =
        pascalCaseRule
            "CamelCaseMethodName"
            "Member name"
            (fun declaration ->
                (declaration.Kind = Member || declaration.Kind = Property)
                && isNameLike declaration.Name)
            "Reports public member names that do not use F# PascalCase."

    let camelCasePropertyName =
        pascalCaseRule
            "CamelCasePropertyName"
            "Property name"
            (fun declaration -> declaration.Kind = Property && isNameLike declaration.Name)
            "Reports property names that do not use F# PascalCase."

    let camelCaseParameterName =
        { Name = "CamelCaseParameterName"
          DefaultPriority = 4
          DefaultProperties = Map.empty
          Description = "Reports formal parameter names that do not use F# camelCase."
          Check =
            fun file selection ->
                defsFor
                    (fun declaration -> declaration.Kind = Parameter && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if startsWithLower declaration.Name then
                            None
                        else
                            Some(sprintf "Parameter name '%s' should use camelCase." declaration.Name)) }

    let camelCaseVariableName =
        { Name = "CamelCaseVariableName"
          DefaultPriority = 4
          DefaultProperties = Map.empty
          Description = "Reports local value names that do not use F# camelCase."
          Check =
            fun file selection ->
                defsFor
                    (fun declaration ->
                        (declaration.Kind = Value || declaration.Kind = Function)
                        && not declaration.IsLiteral
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if startsWithLower declaration.Name then
                            None
                        else
                            Some(sprintf "Variable name '%s' should use camelCase." declaration.Name)) }

    let all =
        [ cyclomaticComplexity
          nPathComplexity
          excessiveMethodLength
          excessiveClassLength
          excessiveParameterList
          excessivePublicCount
          tooManyFields
          tooManyMethods
          tooManyPublicMethods
          excessiveClassComplexity
          shortClassName
          longClassName
          shortVariable
          longVariable
          shortMethodName
          constantNamingConventions
          booleanGetMethodName
          unusedPrivateField
          unusedLocalVariable
          unusedPrivateMethod
          unusedFormalParameter
          booleanArgumentFlag
          elseExpression
          staticAccess
          ifStatementAssignment
          duplicatedArrayKey
          exitExpression
          gotoStatement
          countInLoopExpression
          developmentCodeFragment
          emptyCatchBlock
          couplingBetweenObjects
          globalVariable
          lackOfCohesionOfMethods
          camelCaseClassName
          camelCaseMethodName
          camelCasePropertyName
          camelCaseParameterName
          camelCaseVariableName ]

    let byName =
        all
        |> List.map (fun definition -> definition.Name.ToLowerInvariant(), definition)
        |> Map.ofList
