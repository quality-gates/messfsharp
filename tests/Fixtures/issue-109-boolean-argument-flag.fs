module Issue109

let printOnly (item: bool) =
    if true then
        ()

    printfn "Value: %b" item

let thenBranch (item: bool) =
    if true then
        printfn "Value: %b" item

let ifCondition (item: bool) = if item then 1 else 0

let elifCondition (count: int) (item: bool) =
    if count = 0 then 1
    elif item then 2
    else 0

let matchTarget (item: bool) =
    match item with
    | true -> 1
    | false -> 0
