module Probe

type Box = { mutable Value: int }
let moduleRef = ref 1

let moduleLazy =
    lazy
        (printfn "module lazy"
         1)

let localRef () =
    let cell = ref 1
    cell.Value

let parameterRef (cell: int ref) = cell.Value
let parameterRecord (box: Box) = box.Value
let moduleRefRead () = moduleRef.Value

let knownLazy () =
    let value =
        lazy
            (printfn "local lazy"
             1)

    value.Value

let returnedLazy () =
    (lazy
        (printfn "inline lazy"
         1))
        .Value

let moduleLazyRead () = moduleLazy.Value
let unknownLazy (value: Lazy<int>) = value.Value
let unknownLazyForce (value: Lazy<int>) = value.Force()

let unknownFromCallback (maker: unit -> Lazy<int>) =
    let value = maker ()
    value.Value

module Inner =
    let qualifiedLazy =
        lazy
            (printfn "qualified lazy"
             1)

let qualifiedLazyRead () = Inner.qualifiedLazy.Value

let makeFactory () =
    lazy
        (printfn "factory lazy"
         1)

let fromFactory = makeFactory ()
let forceFactory () = fromFactory.Value

module Aliases =
    let first = makeFactory ()
    let second = first

let forceQualifiedAlias () = Aliases.second.Value

let refAlias (cell: int ref) =
    let alias = cell
    alias.Value

let recordAlias (box: Box) =
    let alias = box
    alias.Value

let unknownLazyAlias (value: Lazy<int>) =
    let alias = value
    alias.Value

let mutable moduleBox: Box = { Value = 1 }
let moduleBoxRead () = moduleBox.Value
let moduleBoxWrite () = moduleBox.Value <- 2

let moduleRefAlias = moduleRef
let moduleBoxAlias = moduleBox
let moduleRefAliasRead () = moduleRefAlias.Value
let moduleBoxAliasRead () = moduleBoxAlias.Value

let returnModuleRef () = moduleRef
let derefReturnedRef () = (returnModuleRef ()).Value

let immutableBox: Box = { Value = 1 }
let immutableBoxAlias = immutableBox
let returnImmutableBox () = immutableBox
let readImmutableBoxAlias () = immutableBoxAlias.Value
let writeImmutableBox () = immutableBox.Value <- 2
let readImmutableBox () = immutableBox.Value
