module Sample

#if NEVER_DEFINED
let BadName = 1
#else
let goodName = 2
#endif

#if !NEVER_DEFINED
let alsoGood = 3
#else
let AlsoBadName = 4
#endif

let used = goodName + alsoGood
