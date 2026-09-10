module Issue84QualifiedDictionary

open System.Collections.Generic

let duplicates =
    System.Collections.Generic.Dictionary<string, int>(
        [ System.Collections.Generic.KeyValuePair<string, int>("same", 1)
          System.Collections.Generic.KeyValuePair<string, int>("same", 2) ]
    )
