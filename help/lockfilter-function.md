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

  This is equivilent to filter(#lambda/testlock(`<key>`, %%0), `<dbrefs>`) but much more efficient, as the lock `<key>` is only parsed/compiled once.

  `<delim>` defaults to a space, and is the delimiter of `<dbrefs>` and the list returned by lockfilter().

- [lockfilter examples]: filtering connected players by name, flag and attribute

## Examples

  Examples:
```sharp
Get all male players with a name starting with 'W'.
think iter(lockfilter(NAME^W*&SEX:M*,lwho()),name(%i0))
Walker WalkerBot Wilco
```

    List all wizroys online:<br>
    > think iter(lockfilter(FLAG^WIZARD|FLAG^ROYALTY,lwho()),name(%i0))<br>
    Sketch Viila Tanaku Raevnos Zebranky Cheetah Walker

    List all players with an IC age > 20.<br>
    > think lockfilter(age:>20,lwho())
    #123 #456 #789
    Note: You can escape the first character of `<key>` using double back slashes, for example, if you are checking for an attribute named +FOO to have the value of BAR on all connected players:<br>
    > think map(#apply/name,lockfilter(\\+FOO:BAR,lwho()))<br>
    Mike Walker Qon


::: seealso
- [LOCKING]
- [LOCK()]
- [ELOCK()]
- [lock keys]
- [FILTER()]
- [TESTLOCK()]
:::
