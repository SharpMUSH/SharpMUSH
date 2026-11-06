<!-- help-article
{
  "corpus": "help",
  "id": "wrapcolumns-function",
  "lookup": "wrapcolumns()",
  "aliases": [
    "wrapcolumns"
  ],
  "sections": []
}
-->
# wrapcolumns()

`wrapcolumns(<text>, <width>, <columns>[, <justify>[, <lborder>[, <mborder>[, <rborder>[, <order>]]]]])`

Word-wraps *<text>* into lines of at most *<width>* characters and sets them out in *<columns>* columns. A line breaks at the last space that fits; a word longer than *<width>* is cut. A line break in *<text>* starts a new line.

- *<justify>* is `L` (the default), `R` or `C`: how each cell is padded to *<width>*.
- *<lborder>*, *<mborder>* and *<rborder>* go before the first column, between columns and after the last.
- *<order>* is 0 (the default) to fill each column top to bottom before the next, or 1 to fill each row left to right.

This function comes from TinyMUX.

```sharp
> think wrapcolumns(one two three four five six, 5, 2, L, <, |, >)
<one  |four >
<two  |five >
<three|six  >
```

::: seealso
- [COLUMNS()]
- [WRAP()]
- [TABLE()]
:::
