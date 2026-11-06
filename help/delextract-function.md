<!-- help-article
{
  "corpus": "help",
  "id": "delextract-function",
  "lookup": "delextract()",
  "aliases": [
    "delextract"
  ],
  "sections": []
}
-->
# delextract()

`delextract(<list>, <first>, <count>[, <delimiter>])`

Returns *<list>* without the *<count>* words that start at position *<first>*, counting from 1. A *<first>* below 1 counts as 1, and a *<count>* below 1 gives the list back unchanged. *<delimiter>* separates the words, a space by default, and joins the ones left.

This function comes from TinyMUX.

```sharp
> think delextract(a b c d e, 2, 2)
a d e
```

::: seealso
- [EXTRACT()]
- [LDELETE()]
- [REMOVE()]
:::
