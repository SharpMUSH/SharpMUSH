<!-- help-article
{
  "corpus": "help",
  "id": "zchildren-function",
  "lookup": "zchildren()",
  "aliases": [
    "ZROOMS()",
    "ZEXITS()",
    "ZTHINGS()",
    "INZONE()",
    "zchildren",
    "zrooms",
    "zexits",
    "zthings",
    "inzone"
  ],
  "sections": []
}
-->
# zchildren()

`zchildren(<zone>)`<br>
`zrooms(<zone>)`<br>
`zexits(<zone>)`<br>
`zthings(<zone>)`<br>
`inzone(<zone>[, <object>])`

These list the objects in `<zone>`, in dbref order: zchildren() every one, zrooms() the rooms, zexits() the exits and zthings() the things. An object in several zones is listed under each. You must control `<zone>` or be a wizard or royalty; otherwise they give `#-1 PERMISSION DENIED`.

inzone() with one argument is zrooms(). With an `<object>` it gives 1 when the object is in `<zone>` and 0 when it is not, or when you cannot examine both.

These functions come from TinyMUX, and inzone()'s second form from RhostMUSH.

```sharp
> think zchildren(#20) / [zrooms(#20)] / [inzone(#20,#31)]
#31 #40 / #40 / 1
```

::: seealso
- [LZONE()]
- [ZFIND()]
- [ZONE()]
- [zones]
:::
