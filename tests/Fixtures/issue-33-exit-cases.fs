module Issue33

open System

module Definitions =
    let exit (code: int) = printfn "%d" code

    type Subsystem() =
        member _.exit(code: int) = ()

    let callMember (s: Subsystem) = s.exit (0)

module ProcessExits =
    let shutdown1 () = Environment.Exit 1
    let shutdown2 code = System.Environment.Exit code
    let shutdown3 code = exit code
    let shutdown4 () = exit -1
