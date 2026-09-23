module ExplicitnessEdges

open System
open System.Collections.Generic

let mutable total = 0
let mutable capacity = 0
let cache: Dictionary<string, int> = makeCache ()
let counter = ref 0

let readName () = stdin.ReadLine()

let writeName (name: string) = stdout.WriteLine name

let countdown (n: int) =
    let mutable n = n

    while n > 0 do
        n <- n - 1

    n

let snapshot () =
    let total = total
    total * 2

let publish (xs: int list) =
    total <- xs |> List.fold (fun total x -> total + x) 0

let lookup key = cache.[key]

let tick () = incr counter

let make () = Dictionary<string, int>(capacity = 3)

let check (config: Config) = config.File.Exists

let moveTo (path: string) = Environment.CurrentDirectory <- path

type Registry() =
    static let mutable count = 0
    member val Items = ResizeArray<int>() with get
    member val Size = 0 with get, set

    member this.Label
        with get () = this.Items.Count
        and set (value: int) = this.Items.Add value

    static member Next() =
        count <- count + 1
        count

    member this.Put item = this.Items.Add item
    member this.Twice value = value * 2
    member this.Pass(values: int list) = values |> List.map this.Twice
    member this.Id<'T>(value: 'T) = value
    member this.Generic() = this.Id<int> 3
    member this.Both() = this.Size + total
