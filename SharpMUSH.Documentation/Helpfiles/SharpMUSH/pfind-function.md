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

Works like [pmatch()], except that a dbref finds any object, not only a player. `pfind(#0)` finds room #0, where `pmatch(#0)` finds no player. When nothing matches, pfind() returns `#-1 NO MATCH` and, unlike pmatch(), sends you no message.

This function comes from TinyMUX.

::: seealso
- [PMATCH()]
- [LOCATE()]
:::
