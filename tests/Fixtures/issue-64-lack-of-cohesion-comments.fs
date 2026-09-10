module Issue64

type Service() =
    let firstState = 0
    let secondState = 0
    member _.First() = firstState

    member _.Second() =
        // firstState is not used here
        let description = "firstState is not used here"
        secondState

type CohesiveService() =
    let sharedState = 0
    member _.First() = sharedState
    member _.Second() = sharedState
