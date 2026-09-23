module ExplicitnessModules

module State =
    let mutable count = 0
    let items = ResizeArray<int>()

let readQualified () = State.count

let addQualified item = State.items.Add item

let count = 5

let readShadowed () = count

module Inner =
    let items = [ 1 ]
    let readInner () = items

open State

let readOpened () = items.Count

let mutable log = 0

let localFunction x =
    let log y = y + 1
    log x

let recursiveLocal n =
    let rec log k = if k = 0 then 0 else log (k - 1)
    log n

let afterLocal x =
    let run () =
        let log y = y
        log x

    run () + log
