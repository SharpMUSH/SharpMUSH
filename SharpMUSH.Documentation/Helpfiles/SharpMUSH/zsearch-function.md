<!-- help-article
{
  "corpus": "help",
  "id": "zsearch-function",
  "lookup": "zsearch()",
  "aliases": [
    "ZSEARCHOBJID()",
    "zsearch",
    "zsearchobjid"
  ],
  "sections": []
}
-->
# zsearch()

`zsearch(<zone> [<class>=<restriction>][, <low>[, <high>]])`<br>
`zsearchobjid(<zone> [<class>=<restriction>][, <low>[, <high>]])`

Searches the objects in `<zone>`. `<class>=<restriction>` and the dbref range are those of search() in its one-argument form; with none, every object in the zone you may search is given. zsearchobjid() writes each as an objid. A `<zone>` that is not found gives `#-1 INVALID ZONE`.

These functions come from RhostMUSH.

```sharp
> think zsearch(#20 type=room)
#40
```

::: seealso
- [LSEARCH()]
- [SEARCHNG()]
- [LZONE()]
:::
