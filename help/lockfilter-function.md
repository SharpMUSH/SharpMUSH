<!-- help-article
{
  "corpus": "help",
  "id": "lockfilter-function",
  "lookup": "lockfilter()",
  "aliases": [],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "lockfilter examples"}
  ]
}
-->
# lockfilter()
`lockfilter(<key>, <dbrefs>[, <delim>])`

lockfilter() goes through `<dbrefs>` and tests them all against the lock `<key>`, returning a list of all dbrefs that pass the `<key>`.

`<key>` is evaluated from the caller's perspective.

This is equivalent to filter(#lambda/testlock(`<key>`, %%0), `<dbrefs>`) but much more efficient, as the lock `<key>` is only parsed/compiled once.

`<delim>` defaults to a space, and is the delimiter of `<dbrefs>` and the list returned by lockfilter().

- [lockfilter examples]: filtering connected players by name, flag and attribute

## Examples

All male players whose names start with W:

```sharp
> think iter(lockfilter(NAME^W*&SEX:M*,lwho()),name(%i0))
Walker WalkerBot Wilco
```

All connected wizards and royalty:

```sharp
> think iter(lockfilter(FLAG^WIZARD|FLAG^ROYALTY,lwho()),name(%i0))
Sketch Viila Tanaku Raevnos
```

All players with an AGE attribute over 20:

```sharp
> think lockfilter(age:>20,lwho())
#123 #456 #789
```

A backslash before a character in an attribute name makes it part of the name, so an attribute named +FOO is not read as a carry key. Write two backslashes, since `<key>` is evaluated first. All connected players whose +FOO attribute is BAR:

```sharp
> think map(#apply/name,lockfilter(\\+FOO:BAR,lwho()))
Mike Walker Qon
```

::: seealso
- [LOCKING]
- [LOCK()]
- [ELOCK()]
- [lock keys]
- [FILTER()]
- [TESTLOCK()]
:::
