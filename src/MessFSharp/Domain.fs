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

    type Declaration =
        { Name: string
          Kind: DeclarationKind
          Location: SourceLocation
          Parent: string option
          ParentKind: DeclarationKind option
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
          IsFunction: bool
          IsModuleLevel: bool
          IsBoolean: bool
          SuppressedRules: Set<string>
          ParameterCount: int
          ScopeStartLine: int
          ScopeEndLine: int
          BodyStartLine: int
          BodyEndLine: int
          Text: string }

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
          TypeMethods: Map<string, Declaration list> }

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
          Suffixes: string list
          Excludes: string list
          Enable: string list
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
              Suffixes = suffixes
              Excludes = []
              Enable = []
              Only = []
              Disable = []
              IgnoreTests = false
              Strict = false
              Color = false
              Verbose = false
              IgnoreErrorsOnExit = false
              IgnoreViolationsOnExit = false }
