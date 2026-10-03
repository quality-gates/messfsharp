module Pricing

open System.Diagnostics.CodeAnalysis

[<SuppressMessage("messfsharp", "cyclomaticcomplexity")>]
let lowerCase (tier: int) =
    match tier with
    | 0 -> 0.0m
    | 1 -> 0.05m
    | 2 -> 0.10m
    | 3 -> 0.15m
    | 4 -> 0.2m
    | 5 -> 0.25m
    | 6 -> 0.3m
    | 7 -> 0.35m
    | 8 -> 0.4m
    | _ -> 0.5m

[<SuppressMessage("messfsharp", "CyclomaticComplexity")>]
let exactCase (tier: int) =
    match tier with
    | 0 -> 0.0m
    | 1 -> 0.05m
    | 2 -> 0.10m
    | 3 -> 0.15m
    | 4 -> 0.2m
    | 5 -> 0.25m
    | 6 -> 0.3m
    | 7 -> 0.35m
    | 8 -> 0.4m
    | _ -> 0.5m

[<SuppressMessage("messfsharp", "npathcomplexity")>]
let otherRule (tier: int) =
    match tier with
    | 0 -> 0.0m
    | 1 -> 0.05m
    | 2 -> 0.10m
    | 3 -> 0.15m
    | 4 -> 0.2m
    | 5 -> 0.25m
    | 6 -> 0.3m
    | 7 -> 0.35m
    | 8 -> 0.4m
    | _ -> 0.5m
