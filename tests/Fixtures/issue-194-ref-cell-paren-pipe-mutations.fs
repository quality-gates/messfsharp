module Repro

let counter1 = ref 0
let counter2 = ref 0
let counter3 = ref 10
let counter4 = ref 10

let step () =
    incr (counter1)
    counter2 |> incr
    decr (counter3)
    counter4 |> decr
