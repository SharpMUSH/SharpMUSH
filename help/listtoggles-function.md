<!-- help-article
{
  "corpus": "help",
  "id": "listtoggles-function",
  "lookup": "listtoggles()",
  "aliases": [
    "listtoggles"
  ],
  "sections": []
}
-->
# listtoggles()

`listtoggles()`

listtoggles() returns every toggle this game has, sorted by name, each written as its name and its letter in parentheses (see [toggles]). A toggle whose flag is mdark, as RhostMUSH's hidden toggles are after an import, is listed only to those who may see all. The letter is RhostMUSH's.

listtoggles() comes from RhostMUSH.

```sharp
> think listtoggles()
KEEPALIVE(K) MONITOR(M)
```

On a game imported from RhostMUSH the list holds every toggle.

::: seealso
- [ltoggles()]
- [listflags()]
:::
