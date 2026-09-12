namespace MessFSharp

open System
open System.IO
open Domain

module PathDisplay =
    let private pathComparison =
        if OperatingSystem.IsWindows() then
            StringComparison.OrdinalIgnoreCase
        else
            StringComparison.Ordinal

    let private normalizedPath (path: string) =
        let fullPath = Path.GetFullPath(path)
        let root = Path.GetPathRoot(fullPath)

        if String.Equals(fullPath, root, pathComparison) then
            fullPath
        else
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

    let private tryResolveLink (path: string) =
        let tryResolve (fileSystemInfo: FileSystemInfo) =
            try
                if isNull fileSystemInfo.LinkTarget then
                    None
                else
                    let target = fileSystemInfo.ResolveLinkTarget(true)

                    if isNull target then None else Some target.FullName
            with _ ->
                None

        [ FileInfo(path) :> FileSystemInfo; DirectoryInfo(path) :> FileSystemInfo ]
        |> List.tryPick tryResolve

    let private resolvePhysicalPath (path: string) =
        let fullPath = normalizedPath path
        let root = Path.GetPathRoot(fullPath)

        let segments =
            fullPath
                .Substring(root.Length)
                .Split(
                    [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |],
                    StringSplitOptions.RemoveEmptyEntries
                )
            |> Array.toList

        let rec resolve current remaining linkCount =
            if linkCount > 64 then
                fullPath
            else
                match remaining with
                | [] -> normalizedPath current
                | segment :: rest ->
                    let candidate = Path.Combine(current, segment)

                    match tryResolveLink candidate with
                    | Some target -> resolve (normalizedPath target) rest (linkCount + 1)
                    | None -> resolve candidate rest linkCount

        resolve root segments 0

    let private isWithin (basePath: string) (path: string) =
        let prefix =
            if
                basePath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || basePath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            then
                basePath
            else
                basePath + string Path.DirectorySeparatorChar

        String.Equals(basePath, path, pathComparison)
        || path.StartsWith(prefix, pathComparison)

    let private reportPathSeparators (path: string) =
        if Path.DirectorySeparatorChar = '\\' then
            path.Replace('\\', '/')
        else
            path

    let private relativePath basePath path =
        let physicalPath = resolvePhysicalPath path

        if isWithin basePath physicalPath then
            let relative = Path.GetRelativePath(basePath, physicalPath)

            if
                relative = ".."
                || relative.StartsWith(".." + string Path.DirectorySeparatorChar, StringComparison.Ordinal)
            then
                reportPathSeparators physicalPath
            else
                reportPathSeparators relative
        else
            reportPathSeparators physicalPath

    let private location mapPath (location: SourceLocation) =
        { location with
            File = mapPath location.File }

    let report baseDirectory (report: Report) =
        let basePath = baseDirectory |> normalizedPath |> resolvePhysicalPath
        let mapPath = relativePath basePath

        { report with
            Violations =
                report.Violations
                |> List.map (fun violation ->
                    { violation with
                        Location = location mapPath violation.Location })
            Errors =
                report.Errors
                |> List.map (fun error ->
                    { error with
                        File = error.File |> Option.map mapPath
                        Location = error.Location |> Option.map (location mapPath) }) }
