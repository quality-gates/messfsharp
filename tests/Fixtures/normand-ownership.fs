module NormandOwnership

let items = [| 0 |]
let change value = items.[0] <- value
let read () = items.[0]

let aliasChange (values: int array) =
    let alias = values
    alias.[0] <- 1

let rebindChange (values: int array) =
    let alias = values
    alias.[0] <- 1
    let alias = Array.copy values
    alias.[0] <- 2

let copyChange (values: int array) =
    let next = Array.copy values
    next.[0] <- 1
    next

let build () = fun () -> printfn "%d" 1
let invokeNow callback = callback ()
let factory callback = fun () -> callback ()
let table = [| 1; 2 |]
let lookup index = table.[index]
let effectLeaf () = printfn "leaf"
let effectMiddle () = effectLeaf ()
let effectTop () = effectMiddle ()
let unknownLeaf callback = callback ()
let unknownTop callback = unknownLeaf callback

let actionAndUnknown callback =
    printfn "known"
    callback ()

let shallowShared (cells: int ref array) =
    let copied = Array.copy cells
    copied.[0].Value <- 1

let conditionalCopy condition (values: int array) =
    let copied = if condition then Array.copy values else values
    copied.[0] <- 1

let capturedPure amount =
    let fixedValue = 2
    fun value -> value + fixedValue + amount

let shadowedCallable helper =
    let helper value = value + 1
    helper 1

let shadowedParameter helper = helper 1
