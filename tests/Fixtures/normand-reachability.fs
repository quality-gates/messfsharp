module NormandReachability

let deadAction value =
    if false then
        printfn "%d" value

    value

let liveAction value =
    if true then
        printfn "%d" value

    value

let deadUnknown value callback =
    if false then
        callback value

    value

let liveUnknown value callback =
    if true then
        callback value

    value

let literalFalseCopy (values: int array) =
    let copied = if false then values else Array.copy values
    copied.[0] <- 1
    copied

let literalTrueShared (values: int array) =
    let copied = if true then values else Array.copy values
    copied.[0] <- 1
    copied
