<!-- help-article
{
  "corpus": "help",
  "id": "toggledef-command",
  "lookup": "@toggledef",
  "aliases": [],
  "sections": []
}
-->
# @toggledef

`@toggledef[/list] [<pattern>]`<br>
`@toggledef/set <toggle>=<permissions>`<br>
`@toggledef/unset <toggle>=<permissions>`<br>
`@toggledef/see <toggle>=<permissions>`

With no switch, `/list` or `/index`, @toggledef lists each toggle matching `<pattern>` with its RhostMUSH letter and who may set and clear it. `/set` and `/unset` change who may set or clear a toggle, and `/see` who may see it, on the toggle's flag (see [toggles]). MONITOR and KEEPALIVE are SharpMUSH's own flags and cannot be changed this way.

`<permissions>` takes RhostMUSH's words, which may be shortened (`wiz`, `arch`). `god` is God only; `immortal`, `royalty` and `wizard` are wizards; `councilor`, `architect` and `guildmaster` are royalty; `mortal` is anyone; `disabled` and `ignore` let no one. A word with `!` before it takes that level away. Other words are passed over, and when none applies nothing changes. For `/see`, a staff level hides the toggle from everyone who may not see all, and `mortal` shows it again.

`/type` and `/letter` are refused: use `@flag/type` on the toggle's flag. @toggledef needs the config.admin permission. It comes from RhostMUSH.

On a game imported from RhostMUSH:

```sharp
> @toggledef/set VPAGE=wizard
@toggledef: VPAGE updated.
> @toggledef VPAGE
VPAGE(v) set: wizard unset: anyone
```

::: seealso
- [toggles]
- [@flag]
:::
