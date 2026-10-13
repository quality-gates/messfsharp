module IdiomaticTwo

open System

let increment value = value + 1

let choose condition left right = if condition then left else right

let tryParse text =
    try
        Some(Int32.Parse(text))
    with _ ->
        None

let asynchronous value = async { return value }
