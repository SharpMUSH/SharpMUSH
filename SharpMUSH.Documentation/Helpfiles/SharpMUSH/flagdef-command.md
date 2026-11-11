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

@flagdef is [@toggledef] for flags: it lists flags matching `<pattern>` with their letters and who may set and clear them, and changes who may set, clear or see one, with @toggledef's permission words. `<flag>` is a full name or alias, or a RhostMUSH name for one SharpMUSH flag. System flags cannot be changed. Unlike RhostMUSH, `/index` lists as `/list` does, and `/see` shows as `mdark` among the set permissions. `/type` and `/letter` are refused: use [@flag]. You need the config.admin permission.

Under the RhostMUSH dialect (see [compatibility config]) this is @flagdef; everywhere it is @rhost_flagdef.

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
