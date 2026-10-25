<!-- help-article
{
  "corpus": "help",
  "id": "roll-dice",
  "lookup": "roll dice",
  "aliases": [],
  "sections": [
    {
      "id": "roll-dice-terms",
      "heading": "Dice terms",
      "lookup": "roll dice terms"
    },
    {
      "id": "roll-dice-arithmetic",
      "heading": "Arithmetic",
      "lookup": "roll arithmetic"
    }
  ]
}
-->
# Roll Dice

A roll() formula adds up dice terms and numbers.

## Dice terms

`<count>d<sides>` rolls `<count>` dice with `<sides>` sides each and adds them up. Leave the count out for one die.

| Term | Rolls |
|---|---|
| `2d6` | two six-sided dice |
| `d20` | one twenty-sided die |
| `d%` | one hundred-sided die (`d%%` typed into a call) |
| `4dF` | four fate dice, each -1, 0 or +1 |
| `3dc` | three coins, each 0 (tails) or 1 (heads) |
| `(1d4)d6` | 1d4 first, then that many six-sided dice |
| `1d(SIDES)` | one die with as many sides as `SIDES` in `<data>` |

```sharp
> think roll(4dF+2,,text)
4dF[1, 0, -1, 1] + 2 = 3
> think roll(lit((1d4)d6),,text)
(1d4)d6[5, 2, 6] = 13
```

Output: a fate die shows -1, 0 or 1. In the second roll the 1d4 came up 3, so three d6 were rolled.

A count or number of sides that comes out as a fraction is rounded down. A die has at most 1000000 sides, and one call rolls at most 1000 dice.

## Arithmetic

Terms and numbers combine with `+`, `-`, `*`, `/` and `%`, and brackets group as usual. A formula may also use `floor()`, `ceil()`, `round()`, `abs()`, `min()` and `max()`. round() rounds a half up, so 2.5 is 3 and -2.5 is -2.

```sharp
> think roll(lit(floor(1d6/2)),,text)
floor(1d6[5] / 2) = 2
> think roll(lit(max(1d6,1d6)),,text)
max(1d6[2], 1d6[5]) = 5
```

::: seealso
- [roll()]
- [ROLL MODIFIERS]
- [ROLL DATA]
:::
