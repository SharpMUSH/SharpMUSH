<!-- help-article
{
  "corpus": "help",
  "id": "mask-function",
  "lookup": "mask()",
  "aliases": [
    "mask"
  ],
  "sections": []
}
-->
# mask()

`mask(<number>, <number>)`
`mask(<number>, <number>[, <number>...], <operation>)`

Combines whole numbers bit by bit. With two arguments it returns their bitwise AND. With more, the last argument is `<operation>`, and its first character chooses:

- `&`: AND of every number. Any character not listed here, or none, is `&`.
- `|`: OR of every number.
- `^`: XOR of every number.
- `~`: the first number with every bit of the others cleared.
- `1`: the ones' complement of the first number. The other numbers must be numbers, but may be negative, and are not used.
- `2`: the two's complement of the first number.

The answer is an unsigned 32-bit number. A number that is negative, or an argument that is not a number, returns `#-1`. A number with a fraction uses its whole part. mask() comes from RhostMUSH.

```sharp
> think mask(12,10) mask(12,10,|) mask(12,10,^) mask(12,10,~)
8 14 6 4
> think mask(5,0,1)
4294967290
```

Output: 12 is 1100 and 10 is 1010 in binary.

::: seealso
- [band()]
- [bor()]
- [bxor()]
- [tobin()]
:::
