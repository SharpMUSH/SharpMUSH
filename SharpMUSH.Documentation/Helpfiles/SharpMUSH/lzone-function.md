<!-- help-article
{
  "corpus": "help",
  "id": "lzone-function",
  "lookup": "lzone()",
  "aliases": [
    "lzone"
  ],
  "sections": []
}
-->
# lzone()

`lzone(<object>[, <page>[, <objid>]])`

Lists the objects in `<object>` when it is a zone with any, and otherwise the zones `<object>` is in, its first zone first. You must be able to examine `<object>`; if you cannot, or it is not found, lzone() gives `#-1`.

- A `<page>` from 1 gives that page of 400 entries.
- `l` gives the number of pages and the number of entries.
- `~<first>-<count>` gives `<count>` entries starting at the `<first>`.
- A true `<objid>` writes each entry as an objid.

lzone() comes from RhostMUSH.

```sharp
> think lzone(#31) / [lzone(#20,l)] / [lzone(#20,~2-1)]
#20 #21 / 1 2 / #40
```

::: seealso
- [@chzone several zones]
- [ZCHILDREN()]
- [ZONE()]
- [zones]
:::
