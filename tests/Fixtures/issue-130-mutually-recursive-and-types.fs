module Issue130MutuallyRecursiveAndTypes

type Node =
    { Value: int
      Next: Node option }
and T =
    { Root: Node
      Left: T option
      Right: T option }
