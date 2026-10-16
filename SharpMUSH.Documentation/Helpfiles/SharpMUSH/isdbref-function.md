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

  isobjid() returns 1 if `<string>` is the object id of an existing object. If `<string>` is not a full objid, or is the objid of a garbage object, it returns 0.

  isdbref() functions the same, but will also return 1 if `<string>` is the dbref of an existing (or garbage) object.

- [isdbref examples]: dbrefs and objids that do and do not exist

## Examples

  Examples:
```sharp
@stats
100 objects = 20 rooms, 20 exits, 20 things, 20 players, 20 garbage.
The next object to be created will be #33.
```

    > think isdbref(#33)<br>
    1<br>
    > think isobjid(#33:1234567890)<br>
    0

    > think csecs(#1)<br>
    1324654503<br>
    > think isdbref(#1)<br>
    1<br>
    > think isobjid(#)<br>
    0<br>
    > think isdbref(#1:1324654503)<br>
    1<br>
    > think isobjid(#1:1324654503)<br>
    1<br>
    > think isobjid(#1:9876543210)<br>
    0


::: seealso
- [database]
- [OBJIDS]
- [NUM()]
- [OBJID()]
:::
