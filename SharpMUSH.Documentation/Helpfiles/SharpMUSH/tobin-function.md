<!-- help-article
{
  "corpus": "help",
  "id": "tobin-function",
  "lookup": "tobin()",
  "aliases": [
    "tobin",
    "tooct()",
    "tooct",
    "todec()",
    "todec",
    "tohex()",
    "tohex"
  ],
  "sections": []
}
-->
# tobin()

`tobin(<number>)`<br>
`tooct(<number>)`<br>
`todec(<number>)`<br>
`tohex(<number>)`

Write `<number>` in binary, octal, decimal or hexadecimal, as a 32-bit value.

`<number>` is read by how it starts: `0x` is hexadecimal (`0x1F`), a leading `0` octal (`017`), and a second character of `b` binary (`0b101`); anything else is decimal. Reading stops at the first character that is not a digit, and no digits at all is 0. A binary number is the exception: every character after `0b` takes a place, so `todec(0b101x)` is 10, and more than 32 sets every bit.

- `tobin()` always writes 32 digits.
- `tohex()` writes capital letters.
- A negative number is written as its 32-bit two's complement, so `todec(-1)` is `4294967295`.

These functions come from RhostMUSH. [baseconv()] converts between any two bases.

```sharp
> think tobin(5)
00000000000000000000000000000101
> think tohex(255) tooct(8) todec(0x1F) todec(0b101)
FF 10 31 5
```

Output: the same kind of number, written in another base.

::: seealso
- [baseconv()]
- [mask()]
:::
