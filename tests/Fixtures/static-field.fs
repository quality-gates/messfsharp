module StaticField

type Counter() =
    static let mutable count = 0

    static member Increment() =
        count <- count + 1
