module Repro

let checkFailwith x =
    if x < 0 then failwith "negative" else x * 10

let checkRaise x =
    if x < 0 then
        raise (System.Exception "negative")
    else
        x * 10

let checkFailwithf x =
    if x < 0 then failwithf "negative %d" x else x * 10

let checkInvalidArg x =
    if x < 0 then
        invalidArg "x" "cannot be negative"
    else
        x * 10

let checkInvalidOp x =
    if x < 0 then invalidOp "cannot be negative" else x * 10

let checkNullArg x = if x < 0 then nullArg "x" else x * 10

let checkReraise x =
    try
        if x < 0 then reraise () else x * 10
    with _ ->
        x
