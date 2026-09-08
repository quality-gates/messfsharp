module Issue32NestedMap

let myMap =
    Map.ofList
        [ "dup", [ 1 ] // The nested list closes before the next entry.
          "dup", [ 2 ] ]
