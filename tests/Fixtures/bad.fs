module Bad

let unused = 1
let mutable globalState = 0

let updateGlobalState () = globalState <- globalState + 1

let BadFunction value = value

let thisIsAnExtremelyLongLocalBindingNameThatNeedsAttention = globalState + 1

let branching value =
    if value > 0 then
        if value > 10 then 1 else 2
    else
        3

let _ = branching 1
