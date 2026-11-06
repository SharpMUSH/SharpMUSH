<!-- help-article
{
  "corpus": "help",
  "id": "butlast-function",
  "lookup": "butlast()",
  "aliases": [
    "LREST()",
    "butlast",
    "lrest"
  ],
  "sections": []
}
-->
# butlast()

`butlast([<list>[, <delimiter>]])`<br>
`lrest([<list>[, <delimiter>]])`

Returns every word of *<list>* but the last, joined by *<delimiter>* (a space by default). A list of one word or none gives an empty string. butlast() is the other half of [rest()], which leaves out the first word. lrest() is the same function under its other TinyMUX name.

These functions come from TinyMUX.

```sharp
> think butlast(one|two|three, |)
one|two
```

::: seealso
- [REST()]
- [FIRST()]
- [LAST()]
- [EXTRACT()]
:::
