<!-- help-article
{
  "corpus": "help",
  "id": "elementpos-function",
  "lookup": "elementpos()",
  "aliases": [
    "elementpos"
  ],
  "sections": []
}
-->
# elementpos()

`elementpos(<string>, <positions>[, <output separator>])`

elementpos() returns the characters of `<string>` at each of the space-separated `<positions>`, counting from 1. It is elements() for characters instead of words. The characters come back in the order the positions are given, and a position given twice gives its character twice. A position before the first character or after the last is skipped. Characters keep their color.

The characters are separated by `<output separator>`, or by a space if it is not given. An empty `<output separator>` runs them together.

Examples:
```sharp
> think elementpos(abcdefg, 1 4)
a d
> think elementpos(abcdefg, 1 4 7, )
adg
> think elementpos(abcdefg, 4 1 4 9)
d a d
```

elementpos() comes from RhostMUSH and takes the same arguments.

::: seealso
- [ELEMENTS()]
- [MID()]
- [POS()]
:::
