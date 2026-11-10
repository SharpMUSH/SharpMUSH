<!-- help-article
{
  "corpus": "help",
  "id": "pfind-function",
  "lookup": "pfind()",
  "aliases": [
    "pfind"
  ],
  "sections": []
}
-->
# pfind()

`pfind(<name>)`

Works like [pmatch()], except that a dbref finds any object, not only a player. `pfind(#0)` finds room #0, where `pmatch(#0)` finds no player. `pfind(me)` finds you. A dbref written with `*`, as in `pfind(*#0)`, finds only a player. Unlike pmatch(), pfind() never sends you a message: a dbref without `*` that matches nothing returns `#-1`, and anything else that matches nothing returns `#-1 NO MATCH`.

This function comes from TinyMUX.

::: seealso
- [PMATCH()]
- [LOCATE()]
:::
