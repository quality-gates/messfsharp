module StaticAccessSuppressed

[<System.Diagnostics.CodeAnalysis.SuppressMessage("messfsharp", "StaticAccess")>]
let suppressed () = System.DateTime.UtcNow
