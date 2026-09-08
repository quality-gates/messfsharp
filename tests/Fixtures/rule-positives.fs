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
