<!-- help-article
{
  "corpus": "help",
  "id": "fbetween-function",
  "lookup": "fbetween()",
  "aliases": [
    "fbetween"
  ],
  "sections": []
}
-->
# fbetween()

`fbetween(<low>, <high>, <number>[, <inclusive>])`

fbetween() returns 1 when `<number>` lies between `<low>` and `<high>`, and 0 otherwise. All three may be decimals, and are rounded to six decimal places first. `<low>` and `<high>` themselves are outside the range unless `<inclusive>` is 1.

fbetween() comes from RhostMUSH.

```sharp
> think fbetween(10.01, 15.01, 14.5) [fbetween(10.5, 15.2, 15.2)] [fbetween(10.5, 15.2, 15.2, 1)]
1 0 1
```

::: seealso
- [gt()]
- [bound()]
:::
