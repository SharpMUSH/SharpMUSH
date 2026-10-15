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

`@roll[/noeval] <formula>[=<object or JSON>]`

Rolls `<formula>` where everyone in the room sees it, and keeps the roll under a number anyone can check with rollinfo().

```sharp
> @roll 1d20+5
[DICE] Alice rolls 1d20[14] + 5 = 19 (roll 42)
> &stat`dex me=2
> @roll 1dF+@stat.dex
[DICE] Alice rolls 1dF[1] + stat.dex(2) = 3 (roll 43)
> @roll 1d20+@attack=Goblin
[DICE] Alice rolls 1d20[9] + attack(4) = 13 (roll 44)
> @roll/noeval {1d20, 10}kh + 1d6[fire]
[DICE] Alice rolls {1d20[7], 10}kh[(7), 10] + 1d6[4] fire = 14 (roll 45)
```

Output: everyone in the room sees the line, with the roll's number at the end.

A name in `<formula>` is one of your own attributes. After `=`, name another object to read its attributes instead, or give a JSON object; both are read as roll() reads its data. Both sides are evaluated, so a $-command can pass `%0` on. `/noeval` takes `<formula>` as typed, for labels in `[ ]` and pools in `{ }`. `%>` after `@roll` is the roll's number.

::: seealso
- [ROLL RECORDS]
- [roll()]
- [rollinfo()]
:::
