module Repro

let counter1 = ref 0
let counter2 = ref 10

let step () =
    incr counter1
    decr counter2
