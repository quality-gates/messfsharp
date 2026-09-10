module Issue70

let multilineBlockComment x =
    try
        x
    with
    | _ ->
        (*
          explanation
        *)
        ()

let singleLineBlockComment x =
    try
        x
    with
    | _ ->
        (* explanation *)
        ()

let sameLineBlockComment x =
    try
        x
    with
    | _ ->
        (* explanation *) ()
