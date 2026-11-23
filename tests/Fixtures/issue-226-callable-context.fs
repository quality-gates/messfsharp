module CallableContext

let noisy x = printfn "%d" x
let quiet x = x + 1 |> ignore
let choose flag left right = if flag then left else right
let invoke action = action 1

let convergent flag =
    let first = if flag then noisy else noisy
    let second = if flag then first else first
    second 1

let distinctContexts flag =
    let selected = choose flag noisy quiet
    invoke selected

let pureContext flag =
    let selected = choose flag quiet quiet
    invoke selected

let rebinding flag =
    let mutable action = quiet

    if flag then
        action <- noisy

    action 1

let captured flag =
    let action = if flag then noisy else noisy
    let nested () = action 1
    nested ()

let shadow flag =
    let action = noisy
    let result = action
    let action = quiet
    let selected = if flag then action else action
    selected 1
    result 1

let rec cycleA x = cycleB x
and cycleB x = cycleA x

let cyclic flag =
    let action = if flag then cycleA else cycleA
    action 1

let rec aliasA = aliasB
and aliasB = aliasA

let cyclicAlias flag =
    let action = if flag then aliasA else aliasA
    action 1
