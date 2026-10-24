<!-- help-article
{
  "corpus": "help",
  "id": "isdbref-function",
  "lookup": "isdbref()",
  "aliases": ["isobjid()"],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "isdbref examples"}
  ]
}
-->
# isdbref()
`isdbref(<string>)`<br>
`isobjid(<string>)`

isobjid() returns 1 if `<string>` is the objid of an existing object, and 0 if it is not a full objid or no such object exists.

isdbref() returns 1 if `<string>` is the dbref or objid of an existing object. A destroyed object's dbref no longer exists, so isdbref() returns 0 for it.

- [isdbref examples]: dbrefs and objids that do and do not exist

## Examples

```sharp
> think objid(#1)
#1:1792817866185
> think isdbref(#1)
1
> think isdbref(#1:1792817866185)
1
> think isobjid(#1)
0
> think isobjid(#1:1792817866185)
1
> think isobjid(#1:9876543210)
0
> think isobjid(#)
0
```

::: seealso
- [database]
- [OBJIDS]
- [NUM()]
- [OBJID()]
:::
