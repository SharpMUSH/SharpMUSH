<!-- help-article
{
  "corpus": "help",
  "id": "numpos-function",
  "lookup": "numpos()",
  "aliases": [
    "numpos"
  ],
  "sections": []
}
-->
# numpos()

`numpos(<match>, <string>[, <type>[, <colour-aware>]])`

Returns how many times `<match>` appears in `<string>`. Matches may overlap, so `aa` is found three times in `aaaa`. An empty `<match>` is found once for each character. Case matters.

With a `<type>` of 1, it returns how many characters of `<string>` are any of the characters in `<match>`.

`<colour-aware>` is accepted for RhostMUSH code and changes nothing: colour is never compared. numpos() comes from RhostMUSH.

```sharp
> think numpos(t,this is a test)
3
> think numpos(aa,aaaa)
3
> think numpos(st,this is a test,1)
6
```

Output: three t's; three overlapping aa's; six characters that are s or t.

::: seealso
- [pos()]
- [matchall()]
- [creplace()]
:::
