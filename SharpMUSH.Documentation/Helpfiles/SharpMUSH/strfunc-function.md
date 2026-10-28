<!-- help-article
{
  "corpus": "help",
  "id": "strfunc-function",
  "lookup": "strfunc()",
  "aliases": [
    "strfunc"
  ],
  "sections": []
}
-->
# strfunc()

`strfunc(<function>, <list>[, <delimiter>])`

strfunc() calls the function `<function>` with the elements of `<list>` as its arguments. The elements are separated by `<delimiter>`, a space by default. Each element is passed as it stands, so brackets and commas in it are not evaluated again.

strfunc() comes from RhostMUSH.

```sharp
> think strfunc(add, 1 2 3 4)
10
> think strfunc(cat, a\[b\]|c, |)
a[b] c
```

::: seealso
- [fn()]
- [ladd()]
:::
