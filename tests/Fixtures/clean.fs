module Clean

type Person = { Name: string; Age: int }

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float

let add x y = x + y

let pipeline value = value |> add 1

let isAdult age = if age >= 18 then true else false
