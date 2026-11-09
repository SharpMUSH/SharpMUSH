<!-- help-article
{
  "corpus": "help",
  "id": "cloak-function",
  "lookup": "cloak()",
  "aliases": [
    "cloak"
  ],
  "sections": []
}
-->
# cloak()

`cloak(<viewer>, <target>)`

cloak() returns 1 when `<target>` is hidden from `<viewer>`, and 0 when it is not. A target is hidden when it is a DARK wizard or royalty, or a player who has used @hide. A viewer who is a wizard or royalty, or has the See_All power, sees through it. Either name matching nothing gives `#-1`.

Only wizards may use cloak(). It comes from RhostMUSH, where a hidden wizard is said to be cloaked.

```sharp
> think cloak(Bob, Alice)
1
> think cloak(me, Alice)
0
```

::: seealso
- [@hide]
- [hidden()]
:::
