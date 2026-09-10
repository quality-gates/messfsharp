module Issue63

let stringIf (condition: bool) =
    let message = "if "
    condition

let commentIf (condition: bool) =
    // if something
    condition

let blockCommentIf (condition: bool) =
    (* if something *)
    condition

let actualIf (condition: bool) = if condition then 1 else 0
