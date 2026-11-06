<!-- help-article
{
  "corpus": "help",
  "id": "caplist-function",
  "lookup": "caplist()",
  "aliases": [
    "caplist"
  ],
  "sections": []
}
-->
# caplist()

`caplist(<list>[, <delimiter>[, <osep>]])`

Capitalizes the first letter of each word in *<list>*, as in a title. These short words go to lower case instead, unless they are the first or last word: a, an, the, and, or, but, for, nor, of, in, on, at, to, by, is, as, if, it, so, vs, via.

*<delimiter>* separates the words, a space by default. *<osep>* joins them, *<delimiter>* by default. An *<osep>* of `@@` joins them with nothing.

This function comes from TinyMUX.

```sharp
> think caplist(a tale OF two cities)
A Tale of Two Cities
```

::: seealso
- [CAPSTR()]
- [LCSTR()]
- [UCSTR()]
:::
