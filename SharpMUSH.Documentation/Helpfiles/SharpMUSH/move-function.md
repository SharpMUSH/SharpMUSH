<!-- help-article
{
  "corpus": "help",
  "id": "move-function",
  "lookup": "move()",
  "aliases": [],
  "sections": []
}
-->
# move()

`move(<object>, <direction>)`

move() makes `<object>` go through the exit `<direction>`, as if it had typed it, or go home when `<direction>` is `home`. You must control `<object>`; otherwise it is `#-1 PERMISSION DENIED`. move() is a side-effect function.

move() comes from RhostMUSH.

```sharp
> think move(me, home)
There's no place like home...
```

::: seealso
- [goto]
- [home]
- [tel()]
:::
