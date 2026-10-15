<!-- help-article
{
  "corpus": "help",
  "id": "roll-modifiers",
  "lookup": "roll modifiers",
  "aliases": [],
  "sections": [
    {
      "id": "roll-keep",
      "heading": "Keep and drop",
      "lookup": "roll keep"
    },
    {
      "id": "roll-reroll",
      "heading": "Reroll and explode",
      "lookup": "roll reroll"
    },
    {
      "id": "roll-count",
      "heading": "Count successes",
      "lookup": "roll count"
    },
    {
      "id": "roll-limit",
      "heading": "Limit a face",
      "lookup": "roll limit"
    },
    {
      "id": "roll-faces",
      "heading": "Choosing faces",
      "lookup": "roll faces"
    }
  ]
}
-->
# Roll Modifiers

A modifier goes right after a dice term, with no space. Several apply in the order written: `4d6r1kh3` rerolls ones, then keeps the highest three.

In text output each die is marked: `!` exploded, `*` a success, `_` a failure, and a die in round brackets was dropped or rerolled and does not count.

## Keep and drop

| Modifier | Does |
|---|---|
| `kh<n>`, `k<n>` | keep the highest n dice; n is 1 if left out |
| `kl<n>` | keep the lowest n |
| `dl<n>`, `d<n>` | drop the lowest n |
| `dh<n>` | drop the highest n |

```sharp
> think roll(4d6kh3,,text)
4d6kh3[5, (2), 4, 6] = 15
> think roll(2d20kl,,text)
2d20kl[(17), 8] = 8
```

Output: the dropped die is in round brackets. Keeping more dice than were rolled keeps them all, so `2d6kh3` is just two dice.

## Reroll and explode

| Modifier | Does |
|---|---|
| `r` | reroll once each die showing its lowest face |
| `rr` | keep rerolling until the die no longer shows it |
| `x` | a die showing its highest face adds another die, which can do the same |
| `xo` | as `x`, but only the dice first rolled add more |
| `x<n>=<face>` | explode on that face, adding at most n dice |

```sharp
> think roll(4d6r1kh3,,text)
4d6r1kh3[(1), 4, 5, (3), 6] = 15
> think roll(3d6x,,text)
3d6x[6!, 3, 2, 4] = 15
```

Output: the 1 was rerolled into a 4, then the 3 was dropped. The 6 exploded and added the 3 after it. One die explodes or is rerolled at most 100 times in a row.

## Count successes

| Modifier | Does |
|---|---|
| `cs` | the total is how many dice show their highest face |
| `cf` | the total is how many dice show their lowest face |
| `df` | each die showing its lowest face takes 1 away |
| `sf` | each failing die takes its own face away |
| `even`, `odd` | count even or odd dice as successes |
| `ms<n>` | the total minus n (`ms<n` gives n minus the total) |

```sharp
> think roll(10d10cs>=8,,text)
10d10cs>=8[3, 9*, 8*, 1, 10*, 4, 2, 7, 8*, 5] = 4
> think roll(5d10cs>=8df1,,text)
5d10cs>=8df1[9*, 1_, 8*, 4, 10*] = 2
> think roll(1d20ms>=15,,text)
1d20ms>=15[18] = 3
```

Output: in the second roll three successes less one failure leave 2. In the third, 18 beat 15 by 3.

## Limit a face

| Modifier | Does |
|---|---|
| `min<n>` | a die below n counts as n |
| `max<n>` | a die above n counts as n |
| `c<n>` | a coin's call: `3dcc1` counts heads, `3dcc0` tails |

```sharp
> think roll(4d6min2,,text)
4d6min2[2, 5, 2, 3] = 12
```

Output: the two 1s counted as 2.

## Choosing faces

`x`, `xo`, `r`, `rr`, `cs`, `cf`, `df`, `sf` and `ms` can name the faces they act on: a number (`r2`), or a comparison with `=`, `>`, `>=`, `<` or `<=` (`x>=9`, `cs>=8`, `sf<3`). The number may be a bracketed formula, such as `cs>=(@difficulty)`.

::: seealso
- [roll()]
- [ROLL DICE]
- [ROLL POOLS]
:::
