module Pricing

open System.Diagnostics.CodeAnalysis

[<SuppressMessage(category = "messfsharp", checkId = "CyclomaticComplexity")>]
let categoryFirst tier =
    match tier with
    | 0 -> 0
    | 1 -> 1
    | 2 -> 2
    | 3 -> 3
    | 4 -> 4
    | 5 -> 5
    | 6 -> 6
    | 7 -> 7
    | 8 -> 8
    | _ -> 9

[<SuppressMessage(checkId = "CyclomaticComplexity", category = "messfsharp")>]
let checkIdFirst tier =
    match tier with
    | 0 -> 0
    | 1 -> 1
    | 2 -> 2
    | 3 -> 3
    | 4 -> 4
    | 5 -> 5
    | 6 -> 6
    | 7 -> 7
    | 8 -> 8
    | _ -> 9

[<SuppressMessage("messfsharp", "CyclomaticComplexity")>]
let positional tier =
    match tier with
    | 0 -> 0
    | 1 -> 1
    | 2 -> 2
    | 3 -> 3
    | 4 -> 4
    | 5 -> 5
    | 6 -> 6
    | 7 -> 7
    | 8 -> 8
    | _ -> 9

[<SuppressMessage(category = "CyclomaticComplexity", checkId = "OtherRule")>]
let unrelatedCheckId tier =
    match tier with
    | 0 -> 0
    | 1 -> 1
    | 2 -> 2
    | 3 -> 3
    | 4 -> 4
    | 5 -> 5
    | 6 -> 6
    | 7 -> 7
    | 8 -> 8
    | _ -> 9
