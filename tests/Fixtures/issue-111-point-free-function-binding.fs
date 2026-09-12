module Issue111

type Command =
    | Case1
    | Case2
    | Case3
    | Case4
    | Case5
    | Case6
    | Case7
    | Case8
    | Case9
    | Case10
    | Case11
    | Case12
    | Case13
    | Case14
    | Case15
    | Case16
    | Case17
    | Case18
    | Case19
    | Case20
    | Case21
    | Case22
    | Case23
    | Case24
    | Case25
    | Case26
    | Case27
    | Case28
    | Case29
    | Case30
    | Case31

let pointFreeDispatcher =
    function
    | Case1 -> 1
    | Case2 -> 2
    | Case3 -> 3
    | Case4 -> 4
    | Case5 -> 5
    | Case6 -> 6
    | Case7 -> 7
    | Case8 -> 8
    | Case9 -> 9
    | Case10 -> 10
    | Case11 -> 11
    | Case12 -> 12
    | Case13 -> 13
    | Case14 -> 14
    | Case15 -> 15
    | Case16 -> 16
    | Case17 -> 17
    | Case18 -> 18
    | Case19 -> 19
    | Case20 -> 20
    | Case21 -> 21
    | Case22 -> 22
    | Case23 -> 23
    | Case24 -> 24
    | Case25 -> 25
    | Case26 -> 26
    | Case27 -> 27
    | Case28 -> 28
    | Case29 -> 29
    | Case30 -> 30
    | Case31 -> 31

let explicitDispatcher command =
    match command with
    | Case1 -> 1
    | Case2 -> 2
    | Case3 -> 3
    | Case4 -> 4
    | Case5 -> 5
    | Case6 -> 6
    | Case7 -> 7
    | Case8 -> 8
    | Case9 -> 9
    | Case10 -> 10
    | Case11 -> 11
    | Case12 -> 12
    | Case13 -> 13
    | Case14 -> 14
    | Case15 -> 15
    | Case16 -> 16
    | Case17 -> 17
    | Case18 -> 18
    | Case19 -> 19
    | Case20 -> 20
    | Case21 -> 21
    | Case22 -> 22
    | Case23 -> 23
    | Case24 -> 24
    | Case25 -> 25
    | Case26 -> 26
    | Case27 -> 27
    | Case28 -> 28
    | Case29 -> 29
    | Case30 -> 30
    | Case31 -> 31

let pf =
    function
    | Case1 -> "first"
    | _ -> "other"

let plainValue = 42
