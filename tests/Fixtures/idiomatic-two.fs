module IdiomaticTwo

open System

let compose first second value = first (second value)

let choose condition left right = if condition then left else right

let tryParse text =
    try
        Some(Int32.Parse(text))
    with _ ->
        None

let asynchronous value = async { return value }
