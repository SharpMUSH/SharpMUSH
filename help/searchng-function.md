<!-- help-article
{
  "corpus": "help",
  "id": "searchng-function",
  "lookup": "searchng()",
  "aliases": [
    "SEARCHNGOBJID()",
    "SEARCHOBJID()",
    "searchng",
    "searchngobjid",
    "searchobjid"
  ],
  "sections": []
}
-->
# searchng()

`searchng([<player>[, <class>, <restriction>]...])`<br>
`searchngobjid([<player>[, <class>, <restriction>]...])`<br>
`searchobjid([<player>[, <class>, <restriction>]...])`

These are search() with a difference: searchng() leaves out objects being destroyed (those with the GOING flag), searchobjid() writes each object as an objid, and searchngobjid() does both. They take every form search() takes.

These functions come from RhostMUSH.

```sharp
> think searchng(me,type,thing)
#31 #52
```

::: seealso
- [LSEARCH()]
- [ZSEARCH()]
- [OBJID()]
:::
