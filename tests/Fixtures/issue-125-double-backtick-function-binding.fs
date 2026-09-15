module Issue125

let ``calculate something complex`` x =
    match x with
    | 1 -> 10
    | 2 -> 20
    | 3 -> 30
    | 4 -> 40
    | 5 -> 50
    | 6 -> 60
    | 7 -> 70
    | 8 -> 80
    | 9 -> 90
    | 10 -> 100
    | 11 -> 110
    | 12 -> 120
    | _ -> 0

let calculateSomethingComplex2 x =
    match x with
    | 1 -> 10
    | 2 -> 20
    | 3 -> 30
    | 4 -> 40
    | 5 -> 50
    | 6 -> 60
    | 7 -> 70
    | 8 -> 80
    | 9 -> 90
    | 10 -> 100
    | 11 -> 110
    | 12 -> 120
    | _ -> 0

let ``too many params function`` a b c d e f g h i j k l m n o = a + b

let tooManyParams2 a b c d e f g h i j k l m n o = a + b

let ``validate user login`` (isEnabled: bool) = if isEnabled then 1 else 0
