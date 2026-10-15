<!-- help-article
{
  "corpus": "help",
  "id": "nsiter-function",
  "lookup": "nsiter()",
  "aliases": [
    "nsiter"
  ],
  "sections": []
}
-->
# nsiter()

`nsiter(<list>, <pattern>[, <delimiter>])`

nsiter() works like iter(), but runs the results together with nothing between them. It evaluates `<pattern>` once for each word of `<list>`, where "##" stands for the word and "#@" for its position, counting from 1. Words are separated by `<delimiter>`, or by spaces if it is not given.

Examples:
```sharp
> think nsiter(a b c, ##-#@)
a-1b-2c-3
> think nsiter(This is a test, strlen(##))
4214
> think nsiter(lnum(4), --+)
--+--+--+--+
```

nsiter() comes from RhostMUSH and takes the same arguments.

::: seealso
- [iter()]
- [citer()]
- [inf()]
:::
