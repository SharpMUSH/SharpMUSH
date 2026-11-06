<!-- help-article
{
  "corpus": "help",
  "id": "creplace-function",
  "lookup": "creplace()",
  "aliases": [
    "creplace"
  ],
  "sections": [
    {
      "id": "creplace-examples",
      "heading": "Examples",
      "lookup": "creplace examples"
    }
  ]
}
-->
# creplace()

`creplace(<string>, <position>, <replacement>[, <type>[, <count>]])`<br>
`creplace(<string>, <positions>, <characters>)`

The first form writes `<replacement>` into `<string>` from the character at `<position>`, counting from 1. `<type>` says how, by its first letter:

- `o` (the default) overwrites one character of `<string>` for each character of `<replacement>`, and the string grows when `<replacement>` runs past its end.
- `c` overwrites the same way, but stops at the end of `<string>`, so the string never grows.
- `i` inserts `<replacement>`, overwriting nothing.
- `r` replaces everything from `<position>` to the end with `<replacement>`.

Any other `<type>` returns nothing. `<count>`, when it is more than 0 and less than the length of `<replacement>`, uses only that many of its characters. A `<position>` past the end of `<string>` adds `<replacement>` at the end. A `<position>` less than 1 or more than 3999 returns `#-1 VALUE MUST BE > 0 < 3999`.

The second form takes a space-separated list of `<positions>` and overwrites the character at each with the next character of `<characters>`, going back to the first when they run out. A position outside `<string>` is skipped. It takes no `<type>` or `<count>`: given one, it returns `#-1 MULTI-REPLACE OPTION REQUIRES 3 ARGS ONLY.` Its first position must also be between 1 and 3999. The output of [totpos()] works as `<positions>`.

Colour on the characters kept stays with them. creplace() comes from RhostMUSH. See [creplace examples].

## Examples

```sharp
> think creplace(abcdef,3,XY)
abXYef
> think creplace(abcdef,5,XYZ,c)
abcdXY
> think creplace(abcdef,3,XY,i)
abXYcdef
> think creplace(abcdef,3,XY,r)
abXY
> think creplace(abcdef,1 3 5,XY)
XbYdXf
```

Output: each line shows one type, and the last the list form.

::: seealso
- [replace()]
- [mid()]
- [numpos()]
:::
