namespace MessFSharp

open System
open System.IO
open Domain

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "CyclomaticComplexity")>]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "NPathComplexity")>]
module Discovery =
    let private alwaysExcludedDirectoryNames =
        set [ "bin"; "obj"; ".git"; "node_modules" ]

    let private isAlwaysExcludedDirectory (directory: DirectoryInfo) =
        alwaysExcludedDirectoryNames.Contains(directory.Name.ToLowerInvariant())

    let private isIgnoredTestDirectoryName (part: string) =
        part.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
        || part.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)

    let private isIgnoredTestPath (root: string) (path: string) =
        let fileName = Path.GetFileName(path)

        let fileIgnored =
            [ "Test.fs"; "Tests.fs"; "Test.fsx"; "Tests.fsx" ]
            |> List.exists (fun suffix -> fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))

        let relativePath = Path.GetRelativePath(root, path)

        let directoryIgnored =
            relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            |> Array.exists isIgnoredTestDirectoryName

        fileIgnored || directoryIgnored

    let private isExcluded (excludes: string list) (path: string) =
        excludes
        |> List.exists (fun exclusion -> path.IndexOf(exclusion, StringComparison.OrdinalIgnoreCase) >= 0)

    let private hasSuffix (suffixes: string list) (path: string) =
        suffixes
        |> List.exists (fun suffix -> path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))

    let private normalize (path: string) =
        let fullPath = Path.GetFullPath(path)
        let root = Path.GetPathRoot(fullPath)

        if String.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) then
            fullPath
        else
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

    let discover (options: AnalysisOptions) =
        let files = ResizeArray<string>()
        let errors = ResizeArray<ProcessingError>()

        let addError path message =
            errors.Add(
                { File = Some path
                  Location = None
                  Message = message }
            )

        let addFile root path =
            let normalized = normalize path

            if
                hasSuffix options.Suffixes normalized
                && not (isExcluded options.Excludes normalized)
                && not (options.IgnoreTests && isIgnoredTestPath root normalized)
            then
                files.Add(normalized)

        let rec visitDirectory root (directory: DirectoryInfo) =
            if
                not (isAlwaysExcludedDirectory directory)
                && not (isExcluded options.Excludes directory.FullName)
            then
                try
                    for file in directory.EnumerateFiles() do
                        addFile root file.FullName

                    for child in directory.EnumerateDirectories() do
                        visitDirectory root child
                with ex ->
                    addError directory.FullName (sprintf "Could not read directory: %s" ex.Message)

        for requestedPath in options.Paths do
            let normalized = normalize requestedPath

            if File.Exists(normalized) then
                if hasSuffix options.Suffixes normalized then
                    addFile normalized normalized
                else
                    addError normalized (sprintf "Path does not match any configured source suffix: %s" normalized)
            elif Directory.Exists(normalized) then
                visitDirectory normalized (DirectoryInfo(normalized))
            else
                addError normalized "Requested path does not exist."

        let sortedFiles =
            files
            |> Seq.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))
            |> Seq.distinct
            |> Seq.toList

        sortedFiles, errors |> Seq.toList
