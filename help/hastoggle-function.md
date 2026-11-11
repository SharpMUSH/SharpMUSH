<!-- help-article
{
  "corpus": "help",
  "id": "hastoggle-function",
  "lookup": "hastoggle()",
  "aliases": [
    "hastoggle"
  ],
  "sections": []
}
-->
# hastoggle()

`hastoggle(<object>, <toggle>)`

hastoggle() returns 1 if `<object>` has `<toggle>` and you may see it, and 0 otherwise, including for a name that is no toggle (see [toggles]).

hastoggle() comes from RhostMUSH.

```sharp
> @create Box
> @toggle Box=MONITOR
> think hastoggle(Box,MONITOR) [hastoggle(Box,KEEPALIVE)]
1 0
```

::: seealso
- [ltoggles()]
- [hasflag()]
:::
