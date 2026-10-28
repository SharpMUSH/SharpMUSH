<!-- help-article
{
  "corpus": "help",
  "id": "keeptype-function",
  "lookup": "keeptype()",
  "aliases": [
    "keeptype",
    "remtype()",
    "remtype"
  ],
  "sections": []
}
-->
# keeptype() and remtype()

`keeptype(<list>, <types>[, <delimiter>[, <output separator>[, <form>]]])`<br>
`remtype(<list>, <types>[, <delimiter>[, <output separator>[, <form>]]])`

keeptype() returns the elements of `<list>` that name an object of one of `<types>`. remtype() returns the others.

`<types>` is a space-separated list of `player`, `thing`, `room`, `exit` and `other`, each of which can be shortened. `other` is an element that names no object you can find. With no type recognised, the list comes back unchanged.

The elements are separated by `<delimiter>`, a space by default, and the result by `<output separator>`, also a space by default. A `<form>` of 1 returns each object as a dbref, and 2 as an objid; otherwise the elements come back as written.

keeptype() and remtype() come from RhostMUSH.

```sharp
> think keeptype(#0 #1 #-1, player)
#1
> think remtype(#0 #1 #-1, player room)
#-1
> think keeptype(#0|#1|#-1, room other, |, -)
#0-#-1
```

::: seealso
- [keepflags()]
- [type()]
- [lsearch()]
:::
