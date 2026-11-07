<!-- help-article
{
  "corpus": "help",
  "id": "andchr-function",
  "lookup": "andchr()",
  "aliases": [
    "andchr",
    "orchr()",
    "orchr",
    "xorchr()",
    "xorchr",
    "notchr()",
    "notchr"
  ],
  "sections": []
}
-->
# andchr()

`andchr(<string>, <characters>[, <each once>])`<br>
`orchr(<string>, <characters>[, <each once>])`<br>
`xorchr(<string>, <characters>[, <each once>])`<br>
`notchr(<string>, <characters>[, <each once>])`

Test which of `<characters>` appear in `<string>`, and return 1 or 0.

- `andchr()`: all of them do. It returns 1 when `<characters>` is empty.
- `orchr()`: at least one does.
- `xorchr()`: exactly one does.
- `notchr()`: none do.

Each character of `<string>` matches only one of `<characters>`, so a character listed twice must appear twice. When `<each once>` is a nonzero number, each of `<characters>` is found if `<string>` holds it at all. Case matters, and colour is not compared.

These functions come from RhostMUSH.

```sharp
> think andchr(abc,ca) andchr(abc,aa) andchr(abc,aa,1)
1 0 1
> think xorchr(abc,ax) orchr(abc,xyz) notchr(abc,xyz)
1 0 1
```

Output: `aa` needs two a's unless `<each once>` is given.

::: seealso
- [strmatch()]
- [numpos()]
:::
