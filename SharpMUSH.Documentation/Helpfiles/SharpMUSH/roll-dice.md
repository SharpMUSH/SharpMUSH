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
    },
    {
      "id": "roll-dice-conditions",
      "heading": "Conditions",
      "lookup": "roll conditions"
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

`clamp(<value>, <low>, <high>)` keeps a value between two others.

## Conditions

These are the formula functions of Foundry VTT's pf2e system, so its formulas roll as written.

| Function | Gives |
|---|---|
| `eq(a, b)`, `ne(a, b)` | 1 if a equals b (does not equal b), else 0 |
| `gt(a, b)`, `gte(a, b)` | 1 if a is more than b (or equal), else 0 |
| `lt(a, b)`, `lte(a, b)` | 1 if a is less than b (or equal), else 0 |
| `btwn(v, low, high)` | 1 if v is from low to high, else 0 |
| `ternary(c, a, b)` | a if c is not 0, else b |
| `when(c, a)` | a if c is not 0, else 0 |
| `match(...)` | the first when() whose c is not 0, or the first other argument; 0 if none |

```sharp
> think roll(lit(match(when(gte(@level,15),8), when(gte(@level,7),3), 1)),lit({"level":9}),text)
match(when(gte(level(9), 15), 8), when(gte(level(9), 7), 3), 1) = 3
> think roll(lit(ternary(gt(1d6,3),1d4,1d8)),,text)
ternary(gt(1d6[5], 3), 1d4[2], 1d8[7]) = 2
```

Output: every argument is rolled, and the text shows each one; only the chosen one counts.

::: seealso
- [roll()]
- [ROLL MODIFIERS]
- [ROLL DATA]
:::
