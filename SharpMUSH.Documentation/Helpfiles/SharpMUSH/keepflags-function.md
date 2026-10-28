<!-- help-article
{
  "corpus": "help",
  "id": "keepflags-function",
  "lookup": "keepflags()",
  "aliases": [
    "keepflags",
    "remflags()",
    "remflags"
  ],
  "sections": []
}
-->
# keepflags() and remflags()

`keepflags(<list>, <flags>[, <delimiter>[, <output separator>[, <form>]]])`<br>
`remflags(<list>, <flags>[, <delimiter>[, <output separator>[, <form>]]])`

keepflags() returns the objects in `<list>` that have the flags in `<flags>`. remflags() returns the ones that do not.

`<flags>` is a list of flag letters, as andflags() takes, after `&` for all of them or `|` for any of them. Without either it is `#-1 FUNCTION REQUIRES '&' OR '|' FOR FLAG MATCHES`. `P`, `R`, `T` and `E` match a type, and `!` before a letter matches its absence.

An element that names no object is reported as a failed lookup and left out. Each object is returned as a dbref, or as an objid when `<form>` is 1. The result is separated by `<output separator>`, which is `<delimiter>` by default.

keepflags() and remflags() come from RhostMUSH.

```sharp
> think keepflags(#1 #2, &W)
#1
> think remflags(#1 #2, |WR)
> think keepflags(#1 #2, W)
#-1 FUNCTION REQUIRES '&' OR '|' FOR FLAG MATCHES
```

::: seealso
- [keeptype()]
- [andflags()]
- [orflags()]
:::
