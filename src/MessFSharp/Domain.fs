namespace MessFSharp

open System

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "ExcessivePublicCount")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "TooManyFields")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
module Domain =
    type SourceKind =
        | Implementation
        | Signature
        | Script

    type ReportFormat =
        | Text
        | Xml
        | Json
        | Html
        | Ansi
        | GitHub
        | GitLab
        | Checkstyle
        | Sarif

    type SourceLocation =
        { File: string
          StartLine: int
          StartColumn: int
          EndLine: int
          EndColumn: int }

    type SourceFile =
        { FullPath: string
          Kind: SourceKind
          Text: string
          Lines: string array }

    type SyntaxTokenKind =
        | Identifier
        | Keyword
        | Number
        | StringLiteral
        | CharacterLiteral
        | Operator
        | Punctuation

    type SyntaxToken =
        { Text: string
          Kind: SyntaxTokenKind
          Line: int
          Column: int
          EndLine: int
          EndColumn: int }

    type DeclarationKind =
        | Namespace
        | Module
        | Type
        | UnionCase
        | Function
        | Value
        | Member
        | Property
        | Field
        | Parameter
        | Constructor

    type TypeShape =
        | NotAType
        | ClassType
        | RecordType
        | UnionType
        | InterfaceType
        | StructType
        | TypeAbbreviation
        | OtherType

    type ExpressionKind =
        | OrdinaryExpression
        | ApplicationExpression
        | ConditionalExpression
        | MatchExpression
        | MatchClauseExpression
        | LoopExpression
        | ExceptionHandlerExpression
        | LambdaExpression
        | ComputationExpression
        | AssignmentExpression

    type NormalizedExpression =
        { Kind: ExpressionKind
          Location: SourceLocation }

    type LexicalScope =
        { Location: SourceLocation
          Parent: SourceLocation option }

    type SyntacticReference =
        { Name: string
          Location: SourceLocation }

    type FlowDirection =
        | InputFlow
        | OutputFlow

    type FlowSource =
        | SharedState
        | AmbientEffect
        | ArgumentMutation
        | TypeState

    /// Data entering or leaving a function or member other than through its arguments or return value.
    type ImplicitFlow =
        { Direction: FlowDirection
          Source: FlowSource
          Name: string
          Owner: string
          OwnerLine: int
          Location: SourceLocation }

    type Declaration =
        { Name: string
          Kind: DeclarationKind
          Location: SourceLocation
          Parent: string option
          ParentKind: DeclarationKind option
          ParentStartLine: int option
          Accessibility: string
          IsMutable: bool
          IsStatic: bool
          IsPrivate: bool
          IsPublic: bool
          IsCompilerGenerated: bool
          IsIgnored: bool
          IsLiteral: bool
          IsRecord: bool
          IsUnion: bool
          IsClassLike: bool
          IsInterface: bool
          TypeShape: TypeShape
          IsFunction: bool
          IsModuleLevel: bool
          IsBoolean: bool
          SuppressedRules: Set<string>
          ParameterCount: int
          ScopeStartLine: int
          ScopeEndLine: int
          BodyStartLine: int
          BodyEndLine: int
          VisibleFrom: int * int
          Text: string }

    let isChildOf (parent: Declaration) (child: Declaration) =
        child.Parent = Some parent.Name
        && (match child.ParentStartLine with
            | Some line -> line = parent.Location.StartLine
            | None -> true)
        && child.Location.StartLine >= parent.Location.StartLine
        && child.Location.StartLine <= parent.ScopeEndLine

    /// Return the declarations that enclose the line: each starts at or before the line and its scope ends at or after it.
    let enclosingDeclarations (declarations: Declaration list) (line: int) =
        declarations
        |> List.filter (fun declaration -> declaration.Location.StartLine <= line && declaration.ScopeEndLine >= line)

    type AnalyzedFile =
        { Source: SourceFile
          Tokens: SyntaxToken array
          Declarations: Declaration list
          ComplexityByDeclaration: Map<string * int, int>
          NPathByDeclaration: Map<string * int, int>
          LineCountByDeclaration: Map<string * int, int>
          ReferenceCounts: Map<string, int>
          ReferenceCountsByDeclaration: Map<string * int, int>
          MutatedNames: Set<string>
          TypeFields: Map<string, Declaration list>
          TypeMethods: Map<string, Declaration list>
          Expressions: NormalizedExpression list
          ExceptionHandlerClauses: SourceLocation list
          LoopIterationRegions: SourceLocation list
          LexicalScopes: LexicalScope list
          SyntacticReferences: SyntacticReference list
          ImplicitFlows: ImplicitFlow list }

    type SymbolContext =
        { Namespace: string option
          Module: string option
          Type: string option
          Member: string option }

    type ProcessingError =
        { File: string option
          Location: SourceLocation option
          Message: string }

    type Violation =
        { Location: SourceLocation
          RuleName: string
          RulesetName: string
          Priority: int
          Description: string
          Context: SymbolContext
          HelpUri: string option }

    /// A compiled report with violations and errors ordered for rendering.
    /// Direct callers must sort violations by ordinal file name, start line,
    /// end line, ordinal rule name, and ordinal description. Sort errors by
    /// ordinal file name, start line, and ordinal message. Use an empty file
    /// name and line 0 when an error has no file or location.
    type Report =
        { ToolName: string
          Version: string
          Violations: Violation list
          Errors: ProcessingError list }

    type RuleSelection =
        { Name: string
          RulesetName: string
          Priority: int
          Properties: Map<string, string> }

    /// The criteria that select rules from the loaded rulesets. Rule names
    /// match without regard to case. Priority 1 is the highest priority, so
    /// MinimumPriority keeps rules with a number equal to or less than it, and
    /// MaximumPriority keeps rules with a number equal to or greater than it.
    type RuleFilter =
        { Only: string list
          Disable: string list
          MinimumPriority: int option
          MaximumPriority: int option }

    type RuleImplementation =
        { Name: string
          DefaultPriority: int
          DefaultProperties: Map<string, string>
          Description: string
          Check: AnalyzedFile -> RuleSelection -> Violation list }

    type AnalysisOptions =
        { Paths: string list
          Format: ReportFormat
          Rulesets: string list
          MinimumPriority: int option
          MaximumPriority: int option
          ReportFile: string option
          BaseDirectory: string option
          Suffixes: string list
          Excludes: string list
          Only: string list
          Disable: string list
          IgnoreTests: bool
          Strict: bool
          Color: bool
          Verbose: bool
          IgnoreErrorsOnExit: bool
          IgnoreViolationsOnExit: bool }

    type Command =
        | Help
        | Version
        | Analyze of AnalysisOptions
        | Invalid of string

    [<RequireQualifiedAccess>]
    module SourceFile =
        let splitLines (text: string) =
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')

        let ofText (path: string) (kind: SourceKind) (text: string) =
            { FullPath = path
              Kind = kind
              Text = text
              Lines = splitLines text }

    [<RequireQualifiedAccess>]
    module SourceKind =
        let ofPath (path: string) =
            match IO.Path.GetExtension(path).ToLowerInvariant() with
            | ".fsi" -> Signature
            | ".fsx" -> Script
            | _ -> Implementation

    [<RequireQualifiedAccess>]
    module ReportFormat =
        let tryParse (value: string) =
            match value.Trim().ToLowerInvariant() with
            | "text" -> Some Text
            | "xml" -> Some Xml
            | "json" -> Some Json
            | "html" -> Some Html
            | "ansi" -> Some Ansi
            | "github" -> Some GitHub
            | "gitlab" -> Some GitLab
            | "checkstyle" -> Some Checkstyle
            | "sarif" -> Some Sarif
            | _ -> None

        let name format =
            match format with
            | Text -> "text"
            | Xml -> "xml"
            | Json -> "json"
            | Html -> "html"
            | Ansi -> "ansi"
            | GitHub -> "github"
            | GitLab -> "gitlab"
            | Checkstyle -> "checkstyle"
            | Sarif -> "sarif"

    module Defaults =
        let suffixes = [ ".fs"; ".fsi"; ".fsx" ]

        let analysisOptions =
            { Paths = []
              Format = Text
              Rulesets = []
              MinimumPriority = None
              MaximumPriority = None
              ReportFile = None
              BaseDirectory = None
              Suffixes = suffixes
              Excludes = []
              Only = []
              Disable = []
              IgnoreTests = false
              Strict = false
              Color = false
              Verbose = false
              IgnoreErrorsOnExit = false
              IgnoreViolationsOnExit = false }
