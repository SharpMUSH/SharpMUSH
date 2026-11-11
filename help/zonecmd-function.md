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

You must control `<object>` for every action but `list`, which needs only that you can examine it. `add` and `replace` also need you to control the zone being added or pass its @lock/chzone, and they reset the WIZARD, ROYALTY and TRUST flags and the powers of anything but a player, as @chzone does. zonecmd() cannot keep them; use @chzone/add/preserve for that. Each action may be shortened to its first letter. zonecmd() is a side-effect function.

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
