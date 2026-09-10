module Sample

type Service() =
    member _.Run(input: int) =
        let localValue = input + 1
        localValue
