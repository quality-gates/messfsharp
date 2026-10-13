module Probe

type Counter = { mutable Count: int }

let private quietRef = ref 1
let quietRefRead () = quietRef.Value

let private quietCounter: Counter = { Count = 1 }
let quietCounterRead () = quietCounter.Count

let private writtenRef = ref 1
let private writtenRefAlias = writtenRef
let writtenRefRead () = writtenRef.Value
let writtenRefAliasRead () = writtenRefAlias.Value
let writtenRefWrite () = writtenRefAlias.Value <- 2

let private writtenCounter: Counter = { Count = 1 }
let private writtenCounterAlias = writtenCounter
let writtenCounterRead () = writtenCounter.Count
let writtenCounterAliasRead () = writtenCounterAlias.Count
let writtenCounterWrite () = writtenCounterAlias.Count <- 2

let sharedCounter: Counter = { Count = 1 }
let sharedCounterAlias = sharedCounter
let returnCounter () = sharedCounter
let sharedCounterRead () = sharedCounter.Count
let sharedCounterAliasRead () = sharedCounterAlias.Count
let sharedCounterWrite () = sharedCounter.Count <- 2
