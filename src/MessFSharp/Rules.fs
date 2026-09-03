namespace MessFSharp

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessivePublicCount")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "GlobalVariable")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "DevelopmentCodeFragment")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessiveClassLength")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyMethods")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CountInLoopExpression")>]
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
                if item.Kind = Member || item.Kind = Property || item.Kind = Constructor then
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

    let private bodyHas (declaration: Declaration) (text: string) =
        declaration.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0

    let private mutationScope (file: AnalyzedFile) (declaration: Declaration) =
        file.Declarations
        |> List.filter (fun candidate ->
            candidate.Name = defaultArg declaration.Parent ""
            && (candidate.Kind = Module || candidate.Kind = Namespace || candidate.Kind = Type)
            && candidate.ScopeStartLine <= declaration.Location.StartLine
            && candidate.ScopeEndLine >= declaration.Location.StartLine)
        |> List.sortByDescending (fun candidate -> candidate.ScopeStartLine)
        |> List.tryHead
        |> Option.map (fun owner -> owner.ScopeStartLine, owner.ScopeEndLine)
        |> Option.defaultValue (1, file.Source.Lines.Length)

    let private bindingScope (file: AnalyzedFile) (declaration: Declaration) =
        if declaration.IsModuleLevel || declaration.Kind = Field then
            mutationScope file declaration
        else
            file.Declarations
            |> List.filter (fun candidate ->
                (candidate.Kind = Function || candidate.Kind = Member)
                && candidate.Location.StartLine < declaration.Location.StartLine
                && candidate.ScopeStartLine <= declaration.Location.StartLine
                && candidate.ScopeEndLine >= declaration.Location.StartLine)
            |> List.sortByDescending (fun candidate -> candidate.Location.StartLine)
            |> List.tryHead
            |> Option.map (fun owner -> owner.ScopeStartLine, owner.ScopeEndLine)
            |> Option.defaultValue (declaration.ScopeStartLine, declaration.ScopeEndLine)

    let private bindingAt (file: AnalyzedFile) name line =
        file.Declarations
        |> List.filter (fun declaration ->
            declaration.Name = name
            && declaration.Location.StartLine <= line
            && (let startLine, endLine = bindingScope file declaration
                line >= startLine && line <= endLine))
        |> List.sortByDescending (fun declaration ->
            let startLine, _ = bindingScope file declaration
            startLine, declaration.Location.StartLine)
        |> List.tryHead

    let private mutationObserved (file: AnalyzedFile) (declaration: Declaration) =
        let startLine, endLine = mutationScope file declaration

        file.Tokens
        |> Array.mapi (fun index token -> index, token)
        |> Array.exists (fun (index, token) ->
            token.Text = declaration.Name
            && token.Line >= startLine
            && token.Line <= endLine
            && bindingAt file declaration.Name token.Line
               |> Option.exists (fun binding ->
                   binding.Name = declaration.Name
                   && binding.Location.StartLine = declaration.Location.StartLine
                   && binding.Kind = declaration.Kind)
            && ((index + 1 < file.Tokens.Length
                 && (file.Tokens[index + 1].Text = "<-" || file.Tokens[index + 1].Text = ":="))
                || (index + 3 < file.Tokens.Length
                    && file.Tokens[index + 1].Text = "."
                    && file.Tokens[index + 2].Text = "Value"
                    && file.Tokens[index + 3].Text = "<-")))

    let private numberedLines (file: AnalyzedFile) =
        file.Source.Lines |> Array.mapi (fun index line -> index + 1, line)

    let private lineIndent (line: string) =
        line
        |> Seq.takeWhile (fun character -> character = ' ' || character = '\t')
        |> Seq.length

    let private splitPropertyValues (value: string) =
        value.Split([| ','; ';' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun item -> item.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.toList

    let private adjustedName (selection: RuleSelection) (name: string) =
        let prefixes = propertyText selection "subtract-prefixes" "" |> splitPropertyValues
        let suffixes = propertyText selection "subtract-suffixes" "" |> splitPropertyValues

        let withoutPrefix =
            prefixes
            |> List.sortByDescending String.length
            |> List.tryFind (fun prefix -> name.StartsWith(prefix, StringComparison.Ordinal))
            |> Option.map (fun prefix -> name.Substring(prefix.Length))
            |> Option.defaultValue name

        suffixes
        |> List.sortByDescending String.length
        |> List.tryFind (fun suffix -> withoutPrefix.EndsWith(suffix, StringComparison.Ordinal))
        |> Option.map (fun suffix -> withoutPrefix.Substring(0, withoutPrefix.Length - suffix.Length))
        |> Option.defaultValue withoutPrefix

    let private ignoredVariableName (selection: RuleSelection) (name: string) =
        let pattern =
            propertyText selection "ignorepattern" (propertyText selection "ignore-pattern" "^(x|xs|f|g|_|_.*)$")

        let exceptions = propertyText selection "exceptions" "" |> splitPropertyValues

        (try
            Regex.IsMatch(name, pattern)
         with _ ->
             false)
        || exceptions
           |> List.exists (fun exceptionName -> String.Equals(exceptionName, name, StringComparison.Ordinal))

    let private hasTerminatingExpression (text: string) =
        Regex.IsMatch(text, "(?i)\\b(?:failwith|raise|Environment\\.Exit)\\b")

    let private isElseFlattenable (file: AnalyzedFile) lineNumber column =
        let currentLine = file.Source.Lines[lineNumber - 1]
        let prefixLength = min currentLine.Length (max 0 (column - 1))
        let currentPrefix = currentLine.Substring(0, prefixLength)

        if not (String.IsNullOrWhiteSpace currentPrefix) then
            Regex.IsMatch(currentPrefix, "(?i)\\bthen\\b[\\s\\S]*\\b(?:failwith|raise|Environment\\.Exit)\\b")
        else
            let elseIndent = lineIndent currentLine
            let mutable index = lineNumber - 2
            let collected = ResizeArray<string>()
            let mutable stopped = false

            while index >= 0 && not stopped do
                let line = file.Source.Lines[index]

                if
                    not (String.IsNullOrWhiteSpace line)
                    && not (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                then
                    let indent = lineIndent line
                    collected.Add(line)

                    if indent <= elseIndent && Regex.IsMatch(line.TrimStart(), "^(?:if|elif)\\b") then
                        stopped <- true
                    elif indent < elseIndent then
                        stopped <- true

                index <- index - 1

            let nonBlankLines =
                collected
                |> Seq.filter (fun line -> lineIndent line > elseIndent)
                |> Seq.toList

            if nonBlankLines.IsEmpty then
                false
            else
                let thenIndent = nonBlankLines |> List.map lineIndent |> List.min
                let lastTopLevelStatement =
                    nonBlankLines
                    |> List.filter (fun line -> lineIndent line = thenIndent)
                    |> List.tryHead
                lastTopLevelStatement |> Option.exists hasTerminatingExpression

    let private linesInsideLoops (file: AnalyzedFile) =
        let lines = file.Source.Lines
        let result = ResizeArray<int>()

        let loopLines =
            file.Tokens
            |> Array.filter (fun token -> token.Kind = Keyword && (token.Text = "for" || token.Text = "while"))
            |> Array.map (fun token -> token.Line)
            |> Array.distinct

        let countLines =
            file.Tokens
            |> Array.windowed 2
            |> Array.choose (fun pair ->
                if pair[0].Text = "." && (pair[1].Text = "Length" || pair[1].Text = "Count") then
                    Some pair[1].Line
                else
                    None)
            |> Set.ofArray

        for loopLineNumber in loopLines do
            let loopIndex = loopLineNumber - 1
            let loopIndent = lineIndent lines[loopIndex]
            let mutable index = loopIndex
            let mutable doneWithLoop = false

            while index < lines.Length && not doneWithLoop do
                let candidate = lines[index]

                if
                    index > loopIndex
                    && not (String.IsNullOrWhiteSpace candidate)
                    && not (candidate.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    && lineIndent candidate <= loopIndent
                then
                    doneWithLoop <- true
                elif countLines.Contains(index + 1) then
                    result.Add(index + 1)

                index <- index + 1

        result |> Seq.distinct |> Seq.toList

    let private constructionBlocks (file: AnalyzedFile) =
        let lines = file.Source.Lines
        let blocks = ResizeArray<int * string>()
        let mapFactories = set [ "ofList"; "ofArray"; "ofSeq" ]

        let lineHasConstruction lineNumber =
            let tokens = file.Tokens |> Array.filter (fun token -> token.Line = lineNumber)

            tokens
            |> Array.exists (fun token -> token.Text = "dict" || token.Text = "Dictionary")
            || (tokens
                |> Array.windowed 3
                |> Array.exists (fun window ->
                    window[0].Text = "Map"
                    && window[1].Text = "."
                    && mapFactories.Contains(window[2].Text)))

        for startIndex in 0 .. lines.Length - 1 do
            let nextLineStartsList =
                startIndex + 1 < lines.Length
                && lines[startIndex + 1].TrimStart().StartsWith("[", StringComparison.Ordinal)

            if
                lineHasConstruction (startIndex + 1)
                && (lines[startIndex].Contains("[", StringComparison.Ordinal) || nextLineStartsList)
            then
                let mutable endIndex = startIndex
                let mutable closed = false

                while endIndex < lines.Length && not closed && endIndex - startIndex < 200 do
                    if
                        file.Tokens
                        |> Array.exists (fun token ->
                            token.Line = endIndex + 1 && token.Kind = Punctuation && token.Text = "]")
                    then
                        closed <- true

                    endIndex <- endIndex + 1

                let endIndex = min lines.Length endIndex
                let text = lines[startIndex .. endIndex - 1] |> String.concat "\n"
                blocks.Add(startIndex + 1, text)

        blocks |> Seq.toList

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

    let private isQualifiedIdentifier (name: string) =
        name.Split('.')
        |> Array.forall (fun part ->
            not (String.IsNullOrWhiteSpace part)
            && Regex.IsMatch(part, "^(?:``[^`]+``|[A-Za-z_][\\w']*)$"))

    let private isNameLike (name: string) =
        not (String.IsNullOrWhiteSpace name)
        && (isQualifiedIdentifier name || not (isOperatorName name))
        && not (name.StartsWith("'", StringComparison.Ordinal))
        && not (name.StartsWith("get_", StringComparison.Ordinal))
        && not (name |> Seq.exists Char.IsWhiteSpace)

    let private startsWithUpper (name: string) =
        isNameLike name
        && (name.Split('.') |> Array.forall (fun part -> Char.IsUpper(part[0])))

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
                    (fun declaration ->
                        (declaration.Kind = Type && declaration.IsClassLike)
                        || declaration.Kind = Module)
                    file
                    selection
                    (fun declaration ->
                        let value =
                            configuredLineCount selection declaration (metric file.LineCountByDeclaration declaration)

                        if value > minimum then
                            Some(sprintf "Type or module length %d exceeds maximum %d lines." value minimum)
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
                    (fun declaration ->
                        declaration.Kind = Function
                        || declaration.Kind = Member
                        || declaration.Kind = Constructor)
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
                            child.Location.StartLine > declaration.Location.StartLine
                            && child.Location.StartLine <= declaration.ScopeEndLine
                            && child.Parent = Some declaration.Name
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
                            (child.Kind = Member || child.Kind = Function || child.Kind = Constructor)
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
                            (child.Kind = Member || child.Kind = Function || child.Kind = Constructor)
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
                |> List.filter (fun declaration ->
                    (declaration.Kind = Type && declaration.IsClassLike)
                    || declaration.Kind = Module)
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
                                (sprintf "Aggregate type or module complexity %d exceeds maximum %d." value maximum)
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
                    (fun declaration ->
                        (declaration.Kind = Type
                         || declaration.Kind = Module
                         || declaration.Kind = Namespace
                         || declaration.Kind = UnionCase)
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if declaration.Name.Length < minimum then
                            Some(
                                sprintf
                                    "Type, module, or union-case name '%s' is shorter than minimum length %d."
                                    declaration.Name
                                    minimum
                            )
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
                    (fun declaration ->
                        (declaration.Kind = Type
                         || declaration.Kind = Module
                         || declaration.Kind = Namespace
                         || declaration.Kind = UnionCase)
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        if declaration.Name.Length > maximum then
                            Some(
                                sprintf
                                    "Type, module, or union-case name '%s' exceeds maximum length %d."
                                    declaration.Name
                                    maximum
                            )
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

                defsFor
                    (fun declaration ->
                        (declaration.Kind = Value
                         || declaration.Kind = Parameter
                         || declaration.Kind = Field)
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        let name = adjustedName selection declaration.Name

                        if name.Length < minimum && not (ignoredVariableName selection declaration.Name) then
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

                defsFor
                    (fun declaration ->
                        (declaration.Kind = Value
                         || declaration.Kind = Parameter
                         || declaration.Kind = Field)
                        && isNameLike declaration.Name)
                    file
                    selection
                    (fun declaration ->
                        let name = adjustedName selection declaration.Name

                        if name.Length > maximum && not (ignoredVariableName selection declaration.Name) then
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
                        && not declaration.IsIgnored
                        || (declaration.Kind = Function
                            && declaration.IsPrivate
                            && declaration.IsModuleLevel
                            && not declaration.IsIgnored))
                    file
                    selection
                    (fun declaration ->
                        if referenceCount file declaration <= 1 then
                            Some(sprintf "Private method or function '%s' is never used." declaration.Name)
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
                let isFlagName (name: string) =
                    name.IndexOf("flag", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("enable", StringComparison.OrdinalIgnoreCase) >= 0
                    || (name.StartsWith("use", StringComparison.OrdinalIgnoreCase)
                        && (name.Length = 3 || Char.IsUpper(name[3]) || name[3] = '_'))

                defsFor
                    (fun declaration ->
                        declaration.Kind = Parameter
                        && declaration.IsBoolean
                        && (isFlagName declaration.Name
                            || (file.Declarations
                                |> List.filter (fun candidate ->
                                    candidate.Name = defaultArg declaration.Parent ""
                                    && (candidate.Kind = Function || candidate.Kind = Member)
                                    && candidate.ScopeStartLine <= declaration.Location.StartLine
                                    && candidate.ScopeEndLine >= declaration.Location.StartLine)
                                |> List.sortByDescending (fun candidate -> candidate.Location.StartLine)
                                |> List.tryHead
                                |> Option.exists (fun candidate ->
                                    (bodyHas candidate "if ")
                                    && file.Tokens
                                       |> Array.filter (fun token ->
                                           token.Text = declaration.Name
                                           && token.Line >= candidate.BodyStartLine
                                           && token.Line <= candidate.BodyEndLine)
                                       |> Array.length
                                       |> fun count -> count > 1))))
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
                file.Tokens
                |> Array.filter (fun token -> token.Text = "else")
                |> Array.choose (fun token ->
                    if isElseFlattenable file token.Line token.Column then
                        Some(
                            violation
                                file
                                selection
                                None
                                token.Line
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
                let tokens = file.Tokens

                tokens
                |> Array.mapi (fun i token ->
                    if
                        i + 4 < tokens.Length
                        && (token.Text = "System" || token.Text = "Microsoft")
                        && tokens[i + 1].Text = "."
                        && tokens[i + 2].Kind = Identifier
                        && startsWithUpper tokens[i + 2].Text
                        && tokens[i + 3].Text = "."
                        && tokens[i + 4].Kind = Identifier
                        && not (i > 0 && tokens[i - 1].Text = "open" && tokens[i - 1].Kind = Keyword)
                    then
                        Some(
                            violation
                                file
                                selection
                                None
                                token.Line
                                "Explicit static access is enabled by an opinionated policy."
                        )
                    else
                        None)
                |> Array.choose id
                |> Array.distinctBy (fun item -> item.Location.StartLine)
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
                let entryKeyPattern =
                    Regex(
                        "^(\"[^\"]*\"|'[^']*'|-?[0-9]+|true|false)\\s*,",
                        RegexOptions.Compiled ||| RegexOptions.IgnoreCase
                    )

                constructionBlocks file
                |> List.choose (fun (lineNumber, text) ->
                    let listContent =
                        match text.IndexOf('[') with
                        | -1 -> text
                        | idx ->
                            let afterOpen = text.Substring(idx + 1).TrimStart('|')

                            match afterOpen.LastIndexOf(']') with
                            | -1 -> afterOpen
                            | closeIdx -> afterOpen.Substring(0, closeIdx).TrimEnd('|')

                    let entries = listContent.Split([| '\n'; ';' |])

                    let keys =
                        entries
                        |> Array.choose (fun (entry: string) ->
                            let trimmed = entry.Trim().TrimStart('(').Trim()
                            let m = entryKeyPattern.Match(trimmed)
                            if m.Success then Some m.Groups[1].Value else None)
                        |> Array.toList

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
                        None) }

    let exitExpression =
        { Name = "ExitExpression"
          DefaultPriority = 3
          DefaultProperties = Map.empty
          Description = "Reports process-exit expressions selected by the design policy."
          Check =
            fun file selection ->
                file.Tokens
                |> Array.mapi (fun index token -> index, token)
                |> Array.choose (fun (index, token) ->
                    let hasNext text =
                        index + 1 < file.Tokens.Length && file.Tokens[index + 1].Text = text

                    let isExitCall =
                        token.Kind = Identifier
                        && token.Text = "exit"
                        && (hasNext "("
                            || (index + 1 < file.Tokens.Length && file.Tokens[index + 1].Kind = Number))

                    let isEnvironmentExitCall =
                        index + 3 < file.Tokens.Length
                        && token.Kind = Identifier
                        && token.Text = "Environment"
                        && file.Tokens[index + 1].Text = "."
                        && file.Tokens[index + 2].Text = "Exit"
                        && file.Tokens[index + 3].Text = "("

                    if isExitCall || isEnvironmentExitCall then
                        Some(
                            violation
                                file
                                selection
                                None
                                token.Line
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
                linesInsideLoops file
                |> List.map (fun lineNumber ->
                    violation file selection None lineNumber "Collection count is evaluated inside a loop.") }

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
                let lines = file.Source.Lines

                numberedLines file
                |> Array.choose (fun (lineNumber, line) ->
                    let pipeIndex = line.IndexOf('|')
                    let withIndex = line.IndexOf("with", StringComparison.OrdinalIgnoreCase)
                    let arrowIndex = line.IndexOf("->", StringComparison.Ordinal)

                    let branchIndex =
                        if pipeIndex >= 0 && pipeIndex < arrowIndex then pipeIndex
                        elif withIndex >= 0 && withIndex < arrowIndex then withIndex
                        else -1

                    let clause =
                        if branchIndex >= 0 then
                            line.Substring(branchIndex).TrimStart()
                        else
                            ""

                    if
                        branchIndex >= 0
                        && (clause.StartsWith("|", StringComparison.Ordinal)
                            || clause.StartsWith("with", StringComparison.OrdinalIgnoreCase))
                    then
                        let afterArrow = line.Substring(arrowIndex + 2).Trim()
                        let branchPrefix = line.Substring(0, branchIndex)
                        let mutable nextIndex = lineNumber
                        let mutable nextMeaningful = None

                        while nextIndex < lines.Length && nextMeaningful.IsNone do
                            let candidate = lines[nextIndex]

                            if
                                not (String.IsNullOrWhiteSpace candidate)
                                && not (candidate.TrimStart().StartsWith("//", StringComparison.Ordinal))
                            then
                                nextMeaningful <- Some(candidate.Trim())

                            nextIndex <- nextIndex + 1

                        let emptyBody = afterArrow = "()" || nextMeaningful = Some "()"

                        let precedingWith =
                            branchPrefix.IndexOf("with", StringComparison.OrdinalIgnoreCase) >= 0
                            || clause.StartsWith("with", StringComparison.OrdinalIgnoreCase)
                            || (lineNumber > 1
                                && lines[lineNumber - 2]
                                    .TrimStart()
                                    .StartsWith("with", StringComparison.OrdinalIgnoreCase))

                        let hasTry =
                            file.Tokens
                            |> Array.exists (fun token ->
                                token.Kind = Keyword
                                && token.Text = "try"
                                && token.Line < lineNumber
                                && token.Line >= max 1 (lineNumber - 50))

                        if emptyBody && precedingWith && hasTry then
                            Some(violation file selection None lineNumber "Exception handler is empty.")
                        else
                            None
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
                        let mutated = mutationObserved file declaration

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
            "Type, module, or union-case name"
            (fun declaration ->
                (declaration.Kind = Type
                 || declaration.Kind = Module
                 || declaration.Kind = Namespace
                 || declaration.Kind = UnionCase)
                && isNameLike declaration.Name)
            "Reports type, module, and union-case names that do not use F# PascalCase."

    let camelCaseMethodName =
        pascalCaseRule
            "CamelCaseMethodName"
            "Member name"
            (fun declaration -> declaration.Kind = Member && isNameLike declaration.Name)
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
