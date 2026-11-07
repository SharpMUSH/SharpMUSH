<!-- help-article
{
  "corpus": "help",
  "id": "vattrcnt-function",
  "lookup": "vattrcnt()",
  "aliases": [
    "vattrcnt"
  ],
  "sections": []
}
-->
# vattrcnt()

`vattrcnt(<object>[, <recount>])`

vattrcnt() returns how many attributes on `<object>` are not built in: the ones a player or a package made, not DESCRIBE, SEX and the others SharpMUSH defines. An object you cannot examine gives `-1`.

vattrcnt() comes from RhostMUSH, which keeps the count on the object and rebuilds it when `<recount>` is 1. SharpMUSH counts every time, so `<recount>` changes nothing.

```sharp
> &NOTES me=one
> &PLANS me=two
> @describe me=A player.
> think vattrcnt(me)
2
```

::: seealso
- [nattr()]
- [attrcnt()]
:::
