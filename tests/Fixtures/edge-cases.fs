module EdgeCases

type private privateThing = { veryLongRecordFieldName: string }

let booleanAndCount (flag: bool) (count: int) = if flag then count else 0

let loopCounts values =
    for value in values do
        let count = value.Count

        printfn "%d" count

let duplicateMap = Map.ofList [ ("same", 1); ("same", 2) ]

let duplicateArray = Map.ofArray [| ("array", 1); ("array", 2) |]

let emptyCatch value =
    try
        value
    with :? System.Exception ->
        ()

let staticUse () = System.Console.WriteLine("hello")

let flatten value =
    if value < 0 then failwith "negative" else value

let exemptLongName = 1
let prefixLongName = 1
let veryLongSuffix = 1
let n = 1
let ordinaryLongNameThatShouldBeReported = 1
