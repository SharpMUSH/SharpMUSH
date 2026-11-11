<!-- help-article
{
  "corpus": "help",
  "id": "setqmatch-function",
  "lookup": "setqmatch()",
  "aliases": [
    "setqmatch",
    "listmatch()",
    "listmatch"
  ],
  "sections": []
}
-->
# setqmatch()

`setqmatch(<string>, <pattern>[, <quiet>])`<br>
`listmatch(<string>, <pattern>[, <delimiter>[, <wildcards>]])`

Match the wildcard `<pattern>` against the whole of `<string>`, case ignored, and return what each wildcard matched. `*` matches any characters and `?` one. Only the first ten wildcards count.

- `setqmatch()` puts what the first wildcard matched in register 0, the second in register 1, and so on. It returns 1 when the pattern matched and set a register, else 0, so a pattern without wildcards gives 0. With a `<quiet>` that starts with `n` it returns nothing.
- `listmatch()` returns what the wildcards matched, joined by the first character of `<delimiter>` (a space by default). `<wildcards>` is a list of wildcard numbers, counting from 0, to keep; they come out in pattern order. It returns nothing when the pattern does not match.

Both come from RhostMUSH.

```sharp
> think [setqmatch(open the door hal,open * ???)] [r(0)]
1 the door
> think listmatch(hit the troll with the sword,hit * with *,/)
the troll/the sword
> think listmatch(hit the troll with the sword,hit * with *,/,1)
the sword
```

Output: 1 and register 0; both wildcards; only the second.

::: seealso
- [strmatch()]
- [setq()]
- [match()]
:::
