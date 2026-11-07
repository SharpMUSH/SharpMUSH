<!-- help-article
{
  "corpus": "help",
  "id": "strlenraw-function",
  "lookup": "strlenraw()",
  "aliases": [
    "strlenraw"
  ],
  "sections": []
}
-->
# strlenraw()

`strlenraw(<string>)`

strlenraw() returns the length of `<string>` with its colour written out as ANSI escape codes, counted in bytes of UTF-8. Text with no colour gives the same as strlen() for plain ASCII.

strlenraw() comes from RhostMUSH.

```sharp
> think strlenraw(abc)
3
> think gt(strlenraw(ansi(r,abc)),3)
1
```

::: seealso
- [strlen()]
- [ansi()]
:::
