<!-- help-article
{
  "corpus": "help",
  "id": "roll-records",
  "lookup": "roll records",
  "aliases": [
    "recorded rolls"
  ],
  "sections": [
    {
      "id": "roll-record-softcode",
      "heading": "Keeping a roll from softcode",
      "lookup": "roll record"
    },
    {
      "id": "roll-checking",
      "heading": "Checking a roll",
      "lookup": "roll checking"
    }
  ]
}
-->
# Roll Records

Anyone can type "Alice rolls 20" with @emit. A kept roll is one the game made and remembers, so anyone can check it by its number.

```sharp
> @roll 2d6+3
[DICE] Alice rolls 2d6[4, 5] + 3 = 12 (roll 57)
```

Output: the room sees the roll and its number, 57.

## Keeping a roll from softcode

roll() keeps a roll when its options say `"record":true`, and then returns JSON with the roll's number as `id`.

```sharp
> think roll(1d20+5,,lit({"record":true}))
{"id":58,"at":"2026-10-15T20:16:40.007Z","roller":"#12:1760558400000","name":"Alice","formula":"1d20+5","total":11,"text":"1d20[6] + 5 = 11","dice":[{"formula":"1d20","faces":20,"total":6,"results":[{"result":6,"active":true}]}]}
> &cmd`roll #20=$+roll *:@remit %l=[setq(r,roll(%0,%#,lit({"record":true})))][notice(DICE,[name(%#)] rolls [json_query(%qr,extract,$.text)] (roll [json_query(%qr,extract,$.id)]))]
> +roll 2d6
[DICE] Alice rolls 2d6[3, 1] = 4 (roll 59)
```

Output: the JSON, then a +roll command on #20 that shows its own line with the number. The roll is Alice's, and rollinfo() names #20 as `by`.

Only a roll is kept: `min` and `max` are refused with `#-1 ONLY A ROLL CAN BE RECORDED`, and asking for `total` or `text` with `"record":true` returns `#-1 A RECORDED ROLL ANSWERS IN JSON`.

## Checking a roll

```sharp
> think json_query(rollinfo(57),extract,$.text)
2d6[4, 5] + 3 = 12
> think json_query(rollinfo(57),extract,$.name)
Alice
```

Output: what roll 57 came to, and who rolled it.

A game can set how long rolls are kept (`SHARPMUSH_HISTORY_ROLLS_MAX_AGE`); a roll older than that returns `#-1 NO SUCH ROLL`. A roll's number is never given to another roll.

::: seealso
- [@roll]
- [rollinfo()]
- [roll()]
- [ROLL OPTIONS]
:::
