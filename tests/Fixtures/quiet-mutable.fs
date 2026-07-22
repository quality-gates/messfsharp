module QuietMutable

let mutable value = 0

let shadowedUpdate () =
    let value = ref 0
    value.Value <- 1
