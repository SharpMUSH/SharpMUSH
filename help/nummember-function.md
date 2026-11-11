<!-- help-article
{
  "corpus": "help",
  "id": "nummember-function",
  "lookup": "nummember()",
  "aliases": [
    "nummember",
    "numwildmatch()",
    "numwildmatch"
  ],
  "sections": []
}
-->
# nummember()

`nummember(<list>, <word>[, <delimiter>])`<br>
`numwildmatch(<list>, <word>[, <delimiter>])`

Count the elements of `<list>` that match `<word>`.

- `nummember()`: elements that are exactly `<word>`. Case matters, and there are no wildcards.
- `numwildmatch()`: elements that, read as a wildcard pattern, match `<word>`. `*` matches any characters and `?` one. Case does not matter.

`<delimiter>` separates the elements, a space by default. Both come from RhostMUSH.

```sharp
> think nummember(a test test repeat a test,test)
3
> think numwildmatch(This test *e* is silly *ed,fred)
2
```

Output: `test` three times; `*e*` and `*ed` both match `fred`.

::: seealso
- [totmember()]
- [member()]
- [matchall()]
:::
