<!-- help-article
{
  "corpus": "help",
  "id": "rollinfo-function",
  "lookup": "rollinfo()",
  "aliases": [
    "rollinfo"
  ],
  "sections": []
}
-->
# rollinfo()

`rollinfo(<number>)`

Returns a kept roll as JSON: who rolled it, when, and every die.

```sharp
> think rollinfo(42)
{"id":42,"at":"2026-10-15T20:14:03.512Z","roller":"#12:1760558400000","name":"Alice","formula":"1d20+5","total":19,"text":"1d20[14] + 5 = 19","dice":[{"formula":"1d20","faces":20,"total":14,"results":[{"result":14,"active":true}]}]}
```

Output: `roller` is the player the roll was made for. When an object's code rolled it, `by` is that object.

Anyone can read any kept roll. A number with no roll behind it, or a roll the game no longer keeps, returns `#-1 NO SUCH ROLL`.

::: seealso
- [ROLL RECORDS]
- [@roll]
- [json_query()]
:::
