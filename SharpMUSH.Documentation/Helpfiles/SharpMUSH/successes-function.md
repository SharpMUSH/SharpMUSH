<!-- help-article
{
  "corpus": "help",
  "id": "successes-function",
  "lookup": "successes()",
  "aliases": [
    "successes"
  ],
  "sections": []
}
-->
# successes()

`successes(<dice>, <difficulty>[, <version>])`

Rolls `<dice>` ten-sided dice and returns how many came up `<difficulty>` or higher, less one for each 1 rolled. `<dice>` must be from 0 to 100.

`<version>` is 1 when left out:

- Version 1: a `<difficulty>` of 0 or less gives `<dice>`, and one above 10 gives 0. Otherwise the result is the count above, and it can be below zero.
- Any other version: when `<difficulty>` is below `<dice>`, a result below zero becomes 0 and a result of zero becomes 1.

This function comes from TinyMUX.

```sharp
> think successes(5,6)
2
```

::: seealso
- [DIE()]
- [ROLL()]
:::
