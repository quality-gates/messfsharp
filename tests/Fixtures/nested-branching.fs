module NestedBranching

let choose value =
    if value > 0 then
        if value > 10 then 1 else 2
    else
        3
