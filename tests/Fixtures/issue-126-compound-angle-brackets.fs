module Issue126CompoundAngleBrackets

open System.Collections.Generic

let duplicates =
    Dictionary<int, List<string>>
        [ KeyValuePair<int, List<string>>(1, [ "a" ])
          KeyValuePair<int, List<string>>(1, [ "b" ]) ]

let deeplyNestedDuplicates =
    Dictionary<int, List<seq<string>>>
        [ KeyValuePair<int, List<seq<string>>>(2, [ seq { "a" } ])
          KeyValuePair<int, List<seq<string>>>(2, [ seq { "b" } ]) ]
