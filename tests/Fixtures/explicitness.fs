module Explicitness

open System

let mutable total = 0
let taxRate = 0.2
let counter = ref 0
let cache = ResizeArray<int>()
let startedAt = DateTime.Now

let addToTotal amount =
    total <- total + amount
    printfn "%d" total

let tax amount = amount * taxRate

let stamp () = DateTime.Now

let bump () = counter.Value <- counter.Value + 1

let remember item = cache.Add item

let append (items: ResizeArray<int>) item = items.Add item

let shadow total = total * 2

let sum values =
    let mutable acc = 0

    for value in values do
        acc <- acc + value

    acc

type Counter(start: int) =
    let mutable count = start
    member val Label = "" with get, set
    member this.Increment() = count <- count + 1
    member this.Current = count
    member this.Rename(label) = this.Label <- label
    member this.Log() = printfn "%s" this.Label
    member this.Snapshot() = total
    static member Create value = Counter(value)
    member _.Plus(x, y) = x + y
