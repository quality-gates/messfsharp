module Issue34

let stringRaise x =
    if x > 0 then
        printfn "We must raise the alarm"
        0
    else
        printfn "All clear"
        1

let stringFailwith x =
    if x then
        printfn "failwith"
        0
    else
        1

let stringEnvironmentExit x =
    if x then
        printfn "Environment.Exit"
        0
    else
        1

let commentFailwith x =
    if x then
        0 // failwith later
    else
        1

let blockCommentRaise x = if x then 0 (* raise later *) else 1

let inlineString x = if x then printfn "raise" else 1

let actualFailwith x =
    if x then
        printfn "stopping"
        failwith "msg"
    else
        1

let actualRaise x =
    if x then
        printfn "stopping"
        raise (System.Exception())
    else
        1

let actualEnvironmentExit x =
    if x then
        printfn "stopping"
        System.Environment.Exit 1
    else
        1

let inlineActual x = if x then failwith "msg" else 1
