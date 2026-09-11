namespace Alpha

type Config =
    val mutable A: int
    val mutable B: int

type Service() =
    member _.One() = 1
    member _.Two() = 2

namespace Beta

type Config =
    val mutable C: int
    val mutable D: int

type Service() =
    member _.Three() = 3
    member _.Four() = 4
