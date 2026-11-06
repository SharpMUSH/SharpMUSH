<!-- help-article
{
  "corpus": "help",
  "id": "str-function",
  "lookup": "str()",
  "aliases": [
    "str"
  ],
  "sections": []
}
-->
# str()

`str(<string>, <match>)`

Returns `<string>` from the first place `<match>` starts to the end, the match included. When `<match>` is not in `<string>`, it returns `#-1`. Case matters.

[after()] is the same without the match itself. str() comes from RhostMUSH.

```sharp
> think str(The quick fox,qu)
quick fox
> think str(The quick fox,QU)
#-1
```

Output: the rest of the string from `qu`, and `#-1` when case does not match.

::: seealso
- [after()]
- [before()]
- [pos()]
:::
