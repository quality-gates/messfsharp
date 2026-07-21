module PatternParameters

let optionValue (Some value) = value

let generic<'T> (value: 'T) = value

let tupled (first, second) = first + second
