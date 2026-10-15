<!-- help-article
{
  "corpus": "help",
  "id": "roll-command",
  "lookup": "@roll",
  "aliases": [],
  "sections": []
}
-->
# @roll

`@roll[/noeval] <formula>[=<data>]`

Rolls `<formula>` where everyone in the room sees it, and keeps the roll under a number anyone can check with rollinfo().

```sharp
> @roll 1d20+5
Alice rolls 1d20[14] + 5 = 19 (roll 42)
> @roll 1dF+@stat.dex=me
Alice rolls 1dF[1] + stat.dex(2) = 3 (roll 43)
> @roll/noeval {1d20, 10}kh + 1d6[fire]
Alice rolls {1d20[7], 10}kh[(7), 10] + 1d6[4] fire = 14 (roll 44)
```

Output: everyone in the room sees the line, with the roll's number at the end.

Both sides are evaluated, so a $-command can pass `%0` on. `/noeval` takes `<formula>` as typed, for labels in `[ ]` and pools in `{ }`. `<data>` is an object or a JSON object, read as roll() reads it. `%>` after `@roll` is the roll's number.

::: seealso
- [ROLL RECORDS]
- [roll()]
- [rollinfo()]
:::
