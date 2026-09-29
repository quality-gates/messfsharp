module ModuleArrayWrites

let items = [| 0 |]

let setIndexer () = items[0] <- 1

let setDotIndexer () = items.[0] <- 1

let setWithArraySet () = Array.set items 0 1

let setArgument (values: int[]) = Array.set values 0 1

let readOnly () = items[0]
