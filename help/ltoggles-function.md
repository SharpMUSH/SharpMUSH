<!-- help-article
{
  "corpus": "help",
  "id": "ltoggles-function",
  "lookup": "ltoggles()",
  "aliases": [
    "ltoggles"
  ],
  "sections": []
}
-->
# ltoggles()

`ltoggles(<object>)`

ltoggles() returns the toggles `<object>` has that you may see, by RhostMUSH's names and in its order (see [toggles]).

ltoggles() comes from RhostMUSH.

```sharp
> @create Box
> @toggle Box=MONITOR
> think ltoggles(Box)
MONITOR
```

::: seealso
- [hastoggle()]
- [listtoggles()]
- [lflags()]
:::
