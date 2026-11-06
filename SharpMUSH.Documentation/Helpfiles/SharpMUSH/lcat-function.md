<!-- help-article
{
  "corpus": "help",
  "id": "lcat-function",
  "lookup": "lcat()",
  "aliases": [
    "lcat"
  ],
  "sections": []
}
-->
# lcat()

`lcat(<list>[, <delimiter>[, <osep>]])`

Joins the words of *<list>* into one string. *<delimiter>* separates the words, a space by default. *<osep>* goes between them, and is nothing unless you give one.

This function comes from TinyMUX.

```sharp
> think lcat(a b c)
abc
> think lcat(a|b|c, |, +)
a+b+c
```

::: seealso
- [CAT()]
- [STRCAT()]
- [GRAPHEMES()]
:::
