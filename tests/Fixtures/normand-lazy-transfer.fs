module NormandLazyTransfer

let make () =
    lazy
        (printfn "body"
         1)

let forceBinding () =
    let value = make ()
    value.Force()

let forceReturn () = (make ()).Force()

let forceValue () =
    let value = make ()
    value.Value
