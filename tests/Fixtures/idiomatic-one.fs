module IdiomaticOne

type Email = Email of string

type Customer = { Name: string; Email: Email }

let parseEmail text = Email text

let mapValues values = values |> List.map string

let rec factorial number =
    if number <= 1 then 1 else number * factorial (number - 1)
