module Issue105RangeBounds

let loop n =
    for i in 1..n do
        printfn "%d" i

let compute () =
    let len = 10

    for i in 0..len do
        printfn "%d" i

let checkMemberAccess () =
    let s = 1.0.ToString()
    let m = 10m.ToString()
    printfn "%s %s" s m
