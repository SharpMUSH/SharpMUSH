<!-- help-article
{
  "corpus": "help",
  "id": "roll-options",
  "lookup": "roll options",
  "aliases": [],
  "sections": [
    {
      "id": "roll-output",
      "heading": "Output",
      "lookup": "roll output"
    },
    {
      "id": "roll-range",
      "heading": "Lowest and highest",
      "lookup": "roll range"
    }
  ]
}
-->
# Roll Options

`<options>`, the third argument to roll(), is one word, or a JSON object with `output`, `mode` and `record`.

| Option | Returns |
|---|---|
| `total` | the total (the default) |
| `text` | each dice term with its dice in square brackets, then `=` and the total |
| `json` | every term and die, for a program to read |
| `min` | the lowest total the formula can come to |
| `max` | the highest total the formula can come to |
| `{"record":true}` | the roll as JSON, kept under a number; see [ROLL RECORDS] |

## Output

```sharp
> think roll(2d6kh1+1,,text)
2d6kh1[5, (2)] + 1 = 6
> think roll(2d6kh1+1,,json)
{"formula":"2d6kh1+1","total":6,"text":"2d6kh1[5, (2)] + 1 = 6","dice":[{"formula":"2d6kh1","faces":6,"total":5,"results":[{"result":5,"active":true},{"result":2,"active":false,"dropped":true}]}]}
```

Output: in JSON, `faces` is the number of sides, `F` for fate dice, `C` for coins or `pool`, and `flavor` is a term's label. A result's `active` is false when it does not count, with `dropped` or `rerolled` saying why; `exploded`, `success` and `failure` appear only when true.

## Lowest and highest

```sharp
> think roll(4d6kh3,,max)
18
> think roll(1d20-1d4,,min)
-3
> think roll(1d20-1d4,,lit({"output":"text","mode":"min"}))
1d20[1] - 1d4[4] = -3
```

Output: the lowest of `1d20-1d4` takes the d20's 1 and the d4's 4.

min and max leave out explosions and rerolls, so `2d6x` reaches at most 12. A formula that could divide by zero has no lowest or highest. Through abs() and %, min and max are limits the roll may not reach, and the text shows only the number.

::: seealso
- [roll()]
- [ROLL MODIFIERS]
- [json()]
:::
