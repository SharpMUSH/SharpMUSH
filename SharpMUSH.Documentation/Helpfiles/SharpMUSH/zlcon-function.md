<!-- help-article
{
  "corpus": "help",
  "id": "zlcon-function",
  "lookup": "zlcon()",
  "aliases": [
    "zlcon"
  ],
  "sections": []
}
-->
# zlcon()

`zlcon(<zone>[, <type>[, <separator>]])`

Gives lcon() of each object in `<zone>`, as one list with `<separator>` between the entries (a space when none is given). `<type>` is lcon()'s. An object whose contents you cannot list adds nothing. A `<zone>` that is not found gives `#-1 INVALID ZONE`.

zlcon() comes from RhostMUSH.

```sharp
> think zlcon(#20)
#52 #53
```

::: seealso
- [LCON()]
- [LZONE()]
- [zones]
:::
