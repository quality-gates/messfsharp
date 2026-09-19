module UppercaseParameters

let add Arg1 Arg2 = Arg2

let f X Y Z = X

let unwrap (Some value) = value

type Calculator() =
    member this.Add Left Right = Right
