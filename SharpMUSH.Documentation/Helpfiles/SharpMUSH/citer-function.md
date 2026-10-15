<!-- help-article
{
  "corpus": "help",
  "id": "citer-function",
  "lookup": "citer()",
  "aliases": [
    "citer"
  ],
  "sections": []
}
-->
# citer()

`citer(<string>, <pattern>[, <output separator>])`

citer() is iter() for the characters of a string instead of the words of a list. It evaluates `<pattern>` once for each character of `<string>`, spaces included, with "##" standing for the character and "#@" for its position, counting from 1. The results are separated by `<output separator>`, or by a space if it is not given or is empty.

Examples:
```sharp
> think citer(boo!, ##-#@)
b-1 o-2 o-3 !-4
> think citer(a b, <##>)
<a> < > <b>
> think citer(bob, ## WHEE ##, @)
b WHEE b@o WHEE o@b WHEE b
```

citer() comes from RhostMUSH and takes the same arguments.

::: seealso
- [iter()]
- [nsiter()]
- [inf()]
:::
