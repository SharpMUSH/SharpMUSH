<!-- help-article
{
  "corpus": "help",
  "id": "reglattri-function",
  "lookup": "reglattri()",
  "aliases": [
    "REGNATTRI()",
    "reglattri",
    "regnattri"
  ],
  "sections": []
}
-->
# reglattri()

`reglattri(<object>, <regexp>)`<br>
`regnattri(<object>, <regexp>)`

reglattri() lists the attributes on *<object>* whose names match the regular expression *<regexp>*, ignoring case; regnattri() counts them. They take TinyMUX's two arguments where [reglattr()] and [regnattr()] take one `<object>/<regexp>`. You see only the attributes you could read with [lattr()].

These functions come from TinyMUX.

```sharp
> think reglattri(me, ^cmd)
CMD_GO CMD_LOOK
```

::: seealso
- [REGLATTR()]
- [REGNATTR()]
- [LATTR()]
- [regexp]
:::
