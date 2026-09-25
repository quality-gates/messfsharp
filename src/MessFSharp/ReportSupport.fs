namespace MessFSharp

open System.Text.Json
open System.Text.Json.Serialization
open Domain

module internal ReportSupport =
    let locationValue (location: SourceLocation) =
        {| file = location.File
           startLine = location.StartLine
           startColumn = location.StartColumn
           endLine = location.EndLine
           endColumn = location.EndColumn |}

    let contextValue context =
        {| ``namespace`` = context.Namespace
           ``module`` = context.Module
           ``type`` = context.Type
           ``member`` = context.Member |}

    let jsonOptions () =
        let options = JsonSerializerOptions(WriteIndented = true)
        options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        options.DefaultIgnoreCondition <- JsonIgnoreCondition.Never
        options
