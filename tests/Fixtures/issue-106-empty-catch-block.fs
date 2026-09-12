module Issue106

let statementsAfterUnit x =
    try
        x
    with ex ->
        ()
        printfn "Caught exception: %O" ex

let multilineGuardEmpty x =
    try
        x
    with :? System.IO.IOException as _ioException when
        // multiline guard condition
        true ->
        ()

let multilineGuardNonEmpty x =
    try
        x
    with :? System.IO.IOException as _ioException when
        // multiline guard condition
        true ->
        printfn "Failed IO"

let multilinePatternEmpty x =
    try
        x
    with
    | :? System.ArgumentException
    | :? System.InvalidOperationException -> ()

let multilinePatternNonEmpty x =
    try
        x
    with
    | :? System.ArgumentException
    | :? System.InvalidOperationException -> printfn "Invalid args or op"
