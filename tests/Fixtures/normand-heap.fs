module NormandHeap

module Case0 =
    let insertThroughAlias (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        let alias = box
        alias.Value <- values
        box.Value.Add 1

module Case1 =
    let put (box: ResizeArray<int> ref) (values: ResizeArray<int>) = box.Value <- values

    let insertThroughHelper (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        put box values
        box.Value.Add 1

module Case2 =
    let uncalledSetter (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        let setter () = box.Value <- values
        box.Value.Add 1

module Case3 =
    let replaceWithPrivate (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        box.Value <- values
        box.Value <- ResizeArray<int>()
        box.Value.Add 1

module Case4 =
    let privateListInsert (values: ResizeArray<int>) =
        let box = ResizeArray<ResizeArray<int>>()
        box.Add values
        box.[0].Add 1

module Case5 =
    let privateListPure () =
        let box = ResizeArray<ResizeArray<int>>()
        box.Add(ResizeArray<int>())
        box.[0].Add 1

module Case6 =
    let make () = ref (ResizeArray<int>())

    let separateFactories (values: ResizeArray<int>) =
        let a = make ()
        let b = make ()
        a.Value <- values
        b.Value.Add 1

module Case7 =
    type Box = { mutable Items: ResizeArray<int> }
    let make (values: ResizeArray<int>) = { Items = values }

    let returnedHolder (values: ResizeArray<int>) =
        let box = make values
        box.Items.Add 1

module Case8 =
    let oldMemberAliasPrivate (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        let alias = box.Value
        box.Value <- values
        alias.Add 1

module Case9 =
    let oldMemberAliasCaller (values: ResizeArray<int>) =
        let box = ref values
        let alias = box.Value
        box.Value <- ResizeArray<int>()
        alias.Add 1

module Case10 =
    type Box =
        { mutable Items: ResizeArray<int>
          Other: ResizeArray<int> }

    let fieldReplacementIndependent (values: ResizeArray<int>) =
        let box = { Items = values; Other = values }
        box.Items <- ResizeArray<int>()
        box.Other.Add 1

module Case11 =
    let conditionalReplacement condition (values: ResizeArray<int>) =
        let box = ref values

        if condition then
            box.Value <- ResizeArray<int>()

        box.Value.Add 1

module Case12 =
    let clear (box: ResizeArray<int> ref) = box.Value <- ResizeArray<int>()

    let helperReplacement (values: ResizeArray<int>) =
        let box = ref values
        clear box
        box.Value.Add 1

module Case13 =
    type Box = { Items: ResizeArray<int>; Count: int }

    let recordCopyCaller (box: Box) =
        let copy = { box with Count = 1 }
        copy.Items.Add 1

module Case14 =
    let eagerLambdaInsert (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        [ 1 ] |> List.iter (fun _ -> box.Value <- values)
        box.Value.Add 1

module Case15 =
    let directLambdaInsert (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        let put () = box.Value <- values
        put ()
        box.Value.Add 1

module Case16 =
    let invoke f = f ()

    let callbackHelperInsert (values: ResizeArray<int>) =
        let box = ref (ResizeArray<int>())
        invoke (fun () -> box.Value <- values)
        box.Value.Add 1

module Case17 =
    let make () =
        let values = ResizeArray<int>()
        let alias = values
        let capturedAliasWrite () = values.Count
        alias.Add 1
        capturedAliasWrite

module Case18 =
    type Box = { Items: ResizeArray<int>; Count: int }

    let recordCopyReplaced (box: Box) =
        let copy = { box with Items = ResizeArray<int>() }
        copy.Items.Add 1

module Case19 =
    let conditionalCallbackReplacement flag (values: ResizeArray<int>) =
        let box = ref values

        if flag then
            [ 1 ] |> List.iter (fun _ -> box.Value <- ResizeArray<int>())

        box.Value.Add 1

module Case20 =
    let make () =
        let values = ResizeArray<int>()
        let mutable alias = values
        let capturedAliasReplaced () = values.Count
        alias <- ResizeArray<int>()
        alias.Add 1
        capturedAliasReplaced
