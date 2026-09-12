module StaticAccessTypePositions

type MyFile = System.IO.FileInfo
type MyList = System.Collections.Generic.List<int>

type Bag<'T>() =
    interface System.Collections.Generic.IEnumerable<'T> with
        member _.GetEnumerator() : System.Collections.Generic.IEnumerator<'T> = failwith "not implemented"
        member _.GetEnumerator() : System.Collections.IEnumerator = failwith "not implemented"

type Registry() =
    inherit System.Collections.Generic.Dictionary<string, int>()

type MyError(message: string) =
    inherit System.Exception(message)

let now () = System.DateTime.UtcNow
let read () = System.IO.File.ReadAllText("input.txt")
