module NormandCallbackTransfer

let apply value mutate = mutate value

let mutator (payload: int array) = payload.[0] <- 1

let privateWrapper () =
    let items = [| 0 |]
    apply items mutator
    items

let callerOwned (items: int array) = apply items mutator

let applyShadow payload mutate = mutate payload

let shadowMutator (payload: int array) = payload.[0] <- 1

let shadowPrivate () =
    let payload = [| 0 |]
    applyShadow payload shadowMutator
    payload

let forward value callback = apply value callback

let forwardCaller (items: int array) = forward items mutator
