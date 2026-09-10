module Sample

type Service =
    member _.Run() =
        let helper value = value + 1
        helper 1

let outer value =
    let helper x = x + 1
    helper value
