<!-- help-article
{
  "corpus": "help",
  "id": "nameqm-function",
  "lookup": "nameqm()",
  "aliases": [
    "nameqm"
  ],
  "sections": []
}
-->
# nameqm()

`nameqm(<register>, <label>[, ... , <register>, <label>])`

nameqm() gives each register its label, as nameq()'s two-argument form does, for each pair in turn. A pair with either half empty is `#-1 ARGUMENTS EXPECTED`, and nameqm() never removes a label. The other errors are nameq()'s.

nameqm() comes from RhostMUSH.

```sharp
> think [setqm(0, red, 1, blue)][nameqm(0, first, 1, second)]%q<first> %q<second>
red blue
```

::: seealso
- [nameq()]
- [setqm()]
:::
