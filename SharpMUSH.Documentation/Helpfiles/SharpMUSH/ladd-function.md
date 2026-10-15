<!-- help-article
{
  "corpus": "help",
  "id": "ladd-function",
  "lookup": "ladd()",
  "aliases": [
    "LSUB()",
    "LMUL()",
    "LDIV()",
    "LAVG()",
    "LMAX()",
    "LMIN()",
    "ladd",
    "lsub",
    "lmul",
    "ldiv",
    "lavg",
    "lmax",
    "lmin"
  ],
  "sections": []
}
-->
# ladd()

`ladd(<list>[, <delimiter>])`<br>
`lsub(<list>[, <delimiter>])`<br>
`lmul(<list>[, <delimiter>])`<br>
`ldiv(<list>[, <delimiter>])`<br>
`lavg(<list>[, <delimiter>])`<br>
`lmax(<list>[, <delimiter>])`<br>
`lmin(<list>[, <delimiter>])`

These do arithmetic on a whole list of numbers at once, the way lmath() does, and never refuse an item for not being a number.

- ladd() adds the numbers together.
- lsub() subtracts each number after the first from the first.
- lmul() multiplies the numbers together.
- ldiv() divides the first number by each of the others in turn. A zero divisor is passed over instead of being an error.
- lavg() returns the average of the numbers.
- lmax() and lmin() return the largest and the smallest number.

Each item counts as the number it starts with, so `1.5x` counts as 1.5 and an item that does not start with a number counts as 0. The items are separated by `<delimiter>`, or by spaces if it is not given or is empty. With another delimiter an empty item is kept, and counts as 0. An empty list gives 0.

Examples:
```sharp
> think ladd(1 2 3 4)
10
> think lsub(10 2 3 1)
4
> think lmul(2|2|2|2, |)
16
> think ldiv(20 2 0 2)
5
> think lavg(2 4 6 8 10)
6
> think lmax(1 10 2 4 6)
10
> think lmin(1 10 2 -4 6)
-4
```

ladd(), lsub(), lmul(), ldiv(), lavg(), lmax() and lmin() come from RhostMUSH and take the same arguments.

::: seealso
- [LMATH()]
- [ADD()]
- [FDIV()]
- [MEAN()]
- [land()]
:::
