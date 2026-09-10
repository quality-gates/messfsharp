module Issue65

let text =
    """
let BadName = 1
"""

(*
let BadName2 = 2
*)

let goodName = text
