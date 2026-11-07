<!-- help-article
{
  "corpus": "help",
  "id": "listcommands-function",
  "lookup": "listcommands()",
  "aliases": [
    "listcommands"
  ],
  "sections": []
}
-->
# listcommands()

`listcommands([<type>])`

listcommands() returns the commands you may run, sorted and in lower case, separated by spaces.

- Type 0, or none, lists the built-in commands.
- Type 1 lists the commands added with @command/add.
- Type 2 lists both, the built-in ones first.
- Type 3 lists the @<attribute> commands that set an attribute, such as @describe.

listcommands() comes from RhostMUSH.

```sharp
> think member(listcommands(),think)
1
```

::: seealso
- [@command]
- [lcmds()]
:::
