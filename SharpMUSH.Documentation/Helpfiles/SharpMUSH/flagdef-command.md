<!-- help-article
{
  "corpus": "help",
  "id": "flagdef-command",
  "lookup": "@flagdef",
  "aliases": [
    "@rhost_flagdef"
  ],
  "sections": []
}
-->
# @flagdef

`@flagdef[/list] [<pattern>]`<br>
`@flagdef/set <flag>=<permissions>`<br>
`@flagdef/unset <flag>=<permissions>`<br>
`@flagdef/see <flag>=<permissions>`

@flagdef is [@toggledef] for flags. With no switch, `/list` or `/index`, it lists each flag matching `<pattern>` with its letter and who may set and clear it. `/set` and `/unset` change who may set or clear a flag, and `/see` who may see it. `<flag>` is a SharpMUSH flag name or a RhostMUSH one that stands for a single SharpMUSH flag. `<permissions>` takes the same words as @toggledef.

A system flag cannot be changed this way; @flagdef reaches the flags a game defined itself, such as those a RhostMUSH import or [@flag] added. `/type` and `/letter` are refused: use `@flag/type` and `@flag/letter`. You need the config.admin permission.

@flagdef is reached under the RhostMUSH dialect (see [compatibility config]); its own name everywhere is @rhost_flagdef.

```sharp
> @flagdef/set SNUFFED=wiz
@flagdef: SNUFFED updated.
> @flagdef/unset SNUFFED=arch
@flagdef: SNUFFED updated.
> @flagdef SNUFFED
SNUFFED() set: wizard unset: royalty
```

::: seealso
- [@flag]
- [@toggledef]
:::
