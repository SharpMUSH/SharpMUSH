<!-- help-article
{
  "corpus": "help",
  "id": "icmd-command",
  "lookup": "@icmd",
  "aliases": [
    "@dialect_icmd"
  ],
  "sections": []
}
-->
# @icmd

`@icmd[/disable|/ignore] <player>=<command>, !<command>, ...`<br>
`@icmd/<on|off|check|clear> <player>`<br>
`@icmd/<droom|iroom> <location>=<command>, !<command>, ...`<br>
`@icmd/<croom|lroom|lallroom> <location>`

@icmd takes built-in commands away from a player, or from every player in a location or its zone. A disabled command answers "Permission denied."; an ignored one is no command at all, so the line goes on to $-commands and then Huh?. `!<command>` gives one back; to move a command between disabled and ignored, give it back first. Aliases and `"`, `:` and `;` reach their commands.

- /disable (the default) and /ignore set a player's commands, /check lists them, /clear removes them all, and /off and /on stop and restart checking them.
- /droom and /iroom set a location's, /lroom lists them, /lallroom lists its zone's too, and /croom removes them.

Only God may use @icmd, and God is never affected. The settings are kept in a wizard-only ICMD attribute owned by God; any other ICMD attribute does nothing. Single-character commands such as `&` cannot be set.

@icmd is TinyMUX's and RhostMUSH's command, reached under those dialects (see [compatibility config]); its own name everywhere is @dialect_icmd.

```sharp
> @icmd Troll=page
PAGE: disabled.
> @icmd/check Troll
Troll: PAGE disabled
```

::: seealso
- [@command]
:::
