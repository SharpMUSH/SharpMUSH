<!-- help-article
{
  "corpus": "help",
  "id": "roll-function",
  "lookup": "roll()",
  "aliases": [
    "roll",
    "dice"
  ],
  "sections": [
    {
      "id": "roll-formulas",
      "heading": "Formula examples",
      "lookup": "roll formulas"
    },
    {
      "id": "roll-typing",
      "heading": "Typing a formula into a call",
      "lookup": "roll typing"
    },
    {
      "id": "roll-errors",
      "heading": "Errors",
      "lookup": "roll errors"
    }
  ]
}
-->
# roll()

`roll(<formula>[, <data>[, <options>]])`

Rolls the dice in `<formula>` and returns the total.

```sharp
> think roll(2d6+5)
12
> think roll(1d20+3,,text)
1d20[14] + 3 = 17
```

Output: a number, or with `text` as the third argument, each die and the total. Each call rolls again, so the numbers in these examples are one possible result.

| Topic | Covers |
|---|---|
| [ROLL DICE] | dice terms, arithmetic and functions |
| [ROLL MODIFIERS] | keeping, rerolling, exploding and counting dice |
| [ROLL POOLS] | choosing among whole formulas, and labels |
| [ROLL DATA] | stats from an object's attributes or a JSON object |
| [ROLL OPTIONS] | the dice as text or JSON, and the lowest and highest totals |
| [ROLL RECORDS] | rolls the game keeps, so anyone can check them |
| [roll formulas] | sample formulas and what each one rolls |

## Formula examples

| Formula | Rolls |
|---|---|
| `2d6+5` | two six-sided dice, plus 5 |
| `4d6kh3` | four six-sided dice, keeping the highest three |
| `2d20kh` | two twenty-sided dice, keeping the better one |
| `2d20kl` | two twenty-sided dice, keeping the worse one |
| `4dF+2` | four fate dice, each -1, 0 or +1, plus 2 |
| `10d10cs>=8` | ten ten-sided dice, counting those that show 8 or more |
| `3d6x` | three six-sided dice; each 6 adds another die |
| `1dF+@stat.dex` | a fate die plus the value at ``STAT`DEX`` in `<data>` |

## Typing a formula into a call

Softcode reads `[ ]`, `{ }`, `%` and commas before roll() sees them. A formula typed into the call that uses them goes inside lit(), and `d%` is typed `d%%`. A formula kept in an attribute and passed with v() or get() needs neither.

```sharp
> think roll(lit({1d20, 10}kh + 5))
17
> think roll(d%%)
42
> &attack me={1d20, 10}kh + 5
> think roll(v(attack))
15
```

## Errors

roll() returns an error starting with #-1 when the formula cannot be read (naming the character where reading stopped), names something `<data>` does not hold, rolls more than 1000 dice, or grows past 1000 terms once its values are filled in. valid(roll, <formula>) checks a formula without rolling it.

```sharp
> think roll(2d6+)
#-1 ROLL SYNTAX ERROR AT CHARACTER 5
> think valid(roll, 2d6kh3+STR)
1
```

::: seealso
- [ROLL DICE]
- [ROLL MODIFIERS]
- [ROLL POOLS]
- [ROLL DATA]
- [ROLL OPTIONS]
- [ROLL RECORDS]
- [die()]
- [rand()]
- [valid()]
:::
