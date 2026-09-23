module Repro

module State =
    let mutable count = 0

let count = 5

open State

let get () = count
