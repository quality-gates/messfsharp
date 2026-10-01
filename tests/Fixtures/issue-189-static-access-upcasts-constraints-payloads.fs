module StaticAccessUpcastsConstraintsPayloads

let upcastValue (x: obj) =
    x :> System.Collections.Generic.IEnumerable<int>

let checkConstraint<'T when 'T :> System.Collections.Generic.IEnumerable<int>> (items: 'T) = ()

type Event = Occurred of System.Collections.Generic.List<string>

let comparer =
    { new System.Collections.Generic.IComparer<int> with
        member _.Compare(x, y) = x.CompareTo(y) }

let now () = System.DateTime.UtcNow
let builder () = new System.Text.StringBuilder()
