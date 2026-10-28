<!-- help-article
{
  "corpus": "help",
  "id": "rxlevel-command",
  "lookup": "@rxlevel",
  "aliases": [
    "@txlevel",
    "@leveldefault"
  ],
  "sections": []
}
-->
# @rxlevel

`@rxlevel[/reset] <object>=<levels>`<br>
`@txlevel[/reset] <object>=<levels>`<br>
`@leveldefault[/list] <object>`

RhostMUSH's and TinyMUX's reality-level commands, over [@reality] layers. Each level is a layer name or alias.

`@rxlevel` adds the levels `<object>` receives, and `@txlevel` those it transmits. A level written `!<level>` is taken away instead. `/reset` clears the set first, so it ends up as exactly `<levels>`.

`@leveldefault` clears the object's reality settings, so it receives and transmits `normal` again. With `/list`, `<object>` is a space-separated list of objects.

The commands need the `reality.admin` permission and control of the object, as `@reality` does. A dbref is taken as it stands, so you can repair an object you cannot perceive.

```sharp
> @txlevel #10=!normal ghost
Reality settings updated for #10:1793154718015.
> @leveldefault #10
Reality settings updated for #10:1793154718015.
```

Output: `#10` first transmits only `ghost`, then `normal` again.

::: seealso
- [rxlevel()]
- [@reality administration]
:::
