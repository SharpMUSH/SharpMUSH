<!-- help-article
{
  "corpus": "help",
  "id": "zonecmd-function",
  "lookup": "zonecmd()",
  "aliases": [
    "zonecmd"
  ],
  "sections": []
}
-->
# zonecmd()

`zonecmd(add, <object>, <zone>)`<br>
`zonecmd(delete, <object>, <zone>)`<br>
`zonecmd(replace, <object>, <old zone>/<new zone>)`<br>
`zonecmd(purge, <object>)`<br>
`zonecmd(list, <object>[, <page>])`

Changes the zones `<object>` is in, as @chzone does, and gives nothing when it worked or an error when it did not.

- `add` puts `<object>` in `<zone>` as well, last; this is @chzone/add.
- `delete` takes it out of `<zone>`; this is @chzone/remove.
- `replace` takes it out of `<old zone>` and puts it in `<new zone>`, last.
- `purge` takes it out of every zone.
- `list` gives the zones `<object>` is in, its first zone first, even when `<object>` is a zone with objects of its own. `<page>` is lzone()'s.

The checks are @chzone's: you must control `<object>`, and to add a zone, control it or pass its @lock/chzone. Each action may be shortened to its first letter. zonecmd() is a side-effect function.

zonecmd() comes from RhostMUSH.

```sharp
> think zonecmd(add,#31,#21)[lzone(#31)]
#20 #21
```

::: seealso
- [@chzone]
- [@chzone several zones]
- [LZONE()]
:::
