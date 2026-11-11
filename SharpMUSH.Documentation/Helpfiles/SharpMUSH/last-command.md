<!-- help-article
{
  "corpus": "help",
  "id": "last-command",
  "lookup": "@last",
  "aliases": [
    "@dialect_last"
  ],
  "sections": []
}
-->
# @last

`@last [<player>]`

@last shows where and when a player last connected, and where and when the last failed attempt to connect as them came from. Without a player, it shows your own. Only the player, those who control them, and wizards and royalty may see it.

It reads the LAST, LASTSITE and LASTFAILED attributes, so it shows the latest connect and failure, not a longer history. A player who is connected sees the connect they are on now. A failure is forgotten at the next successful connect.

@last is TinyMUX's and RhostMUSH's command, reached under those dialects (see [compatibility config]); its own name everywhere is @dialect_last. Without a dialect, `@last <object>=<text>` sets the LAST attribute.

```sharp
> @last
Last connect: from client.example.com on Tue Nov 11 03:00:00 2026.
No failed connect since then.
```

::: seealso
- [LAST]
- [LASTSITE]
- [connlog()]
:::
