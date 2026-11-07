<!-- help-article
{
  "corpus": "help",
  "id": "limath-function",
  "lookup": "limath()",
  "aliases": [
    "limath"
  ],
  "sections": []
}
-->
# limath()

`limath(<operation>, <list>[, <delimiter>])`

The integer form of [lmath()]: applies *<operation>* to every integer in *<list>*. Any item that is not an integer gives `#-1 ARGUMENTS MUST BE INTEGERS`, and an empty list gives 0. The arithmetic is 64-bit and wraps around when it overflows.

*<operation>* is one of:

- `add` or `sum`: the sum.
- `sub`: the first value minus the rest.
- `mul`: the product.
- `div`: the first value divided by each of the rest, dropping any fraction.
- `mod`: the remainder of the first value by each of the rest, with the sign of the divisor, as [mod()] gives it.
- `min` and `max`: the smallest and largest value.
- `median`: the middle value; for an even count, the average of the two middle values, rounded down.

A zero divisor gives `#-1 DIVISION BY ZERO`, and any other operation `#-1 UNKNOWN OPERATION`.

This function comes from TinyMUX.

```sharp
> think limath(div, 100 3)
33
> think limath(median, 1 3 5 7)
4
```

::: seealso
- [LMATH()]
- [DIV()]
- [MOD()]
- [MEDIAN()]
:::
