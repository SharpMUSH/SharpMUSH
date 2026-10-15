<!-- help-article
{
  "corpus": "help",
  "id": "roll-pools",
  "lookup": "roll pools",
  "aliases": [
    "roll labels"
  ],
  "sections": [
    {
      "id": "roll-pool",
      "heading": "Pools",
      "lookup": "roll pool"
    },
    {
      "id": "roll-label",
      "heading": "Labels",
      "lookup": "roll label"
    }
  ]
}
-->
# Roll Pools

A pool picks among whole formulas; a label names a part of a formula.

## Pools

A pool is formulas in braces, separated by commas. Each one's total is a result, and the pool's modifiers keep, drop or count those results the way they would dice. A pool takes keep, drop, count, min, max and ms, but not explode or reroll.

```sharp
> think roll(lit({1d20, 10}kh + 5),,text)
{1d20[7], 10}kh[(7), 10] + 5 = 15
> think roll(lit({2d6, 1d12}kh),,text)
{2d6[3, 5], 1d12[11]}kh[(8), 11] = 11
```

Output: in the first roll the d20 came up 7, so the 10 was kept. In the second, the d12's 11 beat the 8 from 2d6.

## Labels

A label in square brackets after a term names it. Labels show in the text and JSON and do not change the total.

```sharp
> think roll(lit(2d6[fire] + 1d4[cold]),,text)
2d6[3, 4] fire + 1d4[2] cold = 9
```

::: seealso
- [roll()]
- [ROLL MODIFIERS]
- [ROLL OPTIONS]
:::
