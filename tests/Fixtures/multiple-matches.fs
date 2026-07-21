module MultipleMatches

let independent first second =
    let left =
        match first with
        | 0 -> 1
        | 1 -> 2
        | _ -> 3

    let right =
        match second with
        | 0 -> 1
        | _ -> 2

    left + right
