module ExitCases

let exit = 1
let text = "exit (1)"

// exit (1)
let processExit value = System.Environment.Exit(value)

let processExitExpression value = exit (value)
