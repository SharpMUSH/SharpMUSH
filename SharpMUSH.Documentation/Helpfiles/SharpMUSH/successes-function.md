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

Rolls `<dice>` ten-sided dice and returns how many came up `<difficulty>` or higher, less one for each 1 rolled. The result can be below zero. `<dice>` must be from 0 to 100. `<version>` must be a number if given, and changes nothing.

This function comes from TinyMUX.

```sharp
> think successes(5,6)
2
```

::: seealso
- [DIE()]
- [ROLL()]
:::
