<!-- help-article
{
  "corpus": "help",
  "id": "lband-function",
  "lookup": "lband()",
  "aliases": [
    "LBOR()",
    "LBXOR()",
    "lband",
    "lbor",
    "lbxor"
  ],
  "sections": []
}
-->
# lband()

`lband([<list>[, <delimiter>]])`<br>
`lbor([<list>[, <delimiter>]])`<br>
`lbxor([<list>[, <delimiter>]])`

Return the bitwise AND, OR and exclusive OR of every integer in *<list>*. *<delimiter>* separates the integers, a space by default. Any item that is not an integer gives `#-1 ARGUMENTS MUST BE INTEGERS`. An empty list gives -1 from lband() (every bit set) and 0 from lbor() and lbxor().

These functions come from TinyMUX.

```sharp
> think [lband(7 14)] [lbor(1 2 4)] [lbxor(255 15)]
6 7 240
```

::: seealso
- [BAND()]
- [BOR()]
- [BXOR()]
- [LAND()]
:::
