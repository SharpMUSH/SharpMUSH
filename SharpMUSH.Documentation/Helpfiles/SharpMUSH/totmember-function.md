<!-- help-article
{
  "corpus": "help",
  "id": "totmember-function",
  "lookup": "totmember()",
  "aliases": [
    "totmember"
  ],
  "sections": []
}
-->
# totmember()

`totmember(<list>, <word>[, <delimiter>])`

Returns the position, counting from 1, of every element of `<list>` that is exactly `<word>`, separated by spaces. It returns 0 when there is none. Case matters, and there are no wildcards.

`<delimiter>` separates the elements, a space by default. totmember() comes from RhostMUSH.

```sharp
> think totmember(red blue red green,red)
1 3
> think totmember(red blue,Red)
0
```

Output: `red` is the first and third element; `Red` matches nothing.

::: seealso
- [member()]
- [matchall()]
- [totpos()]
:::
