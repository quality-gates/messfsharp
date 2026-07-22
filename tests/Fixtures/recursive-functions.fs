module RecursiveFunctions

let rec even number = number = 0 || BadOdd(number - 1)

and BadOdd number = number <> 0 && even (number - 1)
