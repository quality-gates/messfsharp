module Repro

let log (printfn: string -> unit) = printfn "hello"

let run () =
    let printfn msg = ()
    printfn "hello"

let read (stdin: string) = stdin.Length
