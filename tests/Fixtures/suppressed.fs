module Suppressed

open System.Diagnostics.CodeAnalysis

[<SuppressMessage("messfsharp", "GlobalVariable")>]
let mutable intentionallyShared = 0
