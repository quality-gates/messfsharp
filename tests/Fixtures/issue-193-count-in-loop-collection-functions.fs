module Repro

let check (map: Map<string, int>) (set: Set<string>) (str: string) (items: int list) =
    for x in items do
        ignore (Map.count map)
        ignore (Set.count set)
        ignore (String.length str)
