module RulePositives

[<Literal>]
let badConstant = 1

type NamingExamples() =
    let unusedField = 1
    let externalDependency: System.IO.Stream = null

    member _.getReady: bool = true
    member _.badMember() = 1
    member val badProperty = 1 with get, set

let useParameter (``BadParameter``: int) = ``BadParameter``

// TODO: deliberate development marker for rule acceptance coverage.
let developmentMarker = 1

type DisconnectedState() =
    let first = 1
    let second = 2

    member _.First() = first + 1
    member _.Second() = second + 1
