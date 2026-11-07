<!-- help-article
{
  "corpus": "help",
  "id": "blacklist-command",
  "lookup": "@blacklist",
  "aliases": [],
  "sections": []
}
-->
# @blacklist

`@blacklist[/<list>]`<br>
`@blacklist/add[/<list>] <address>[ <mask>]`<br>
`@blacklist/del[/<list>] <address>[ <mask>]`<br>
`@blacklist/list[/<list>]`<br>
`@blacklist/mask[/<list>]`<br>
`@blacklist/search[/<list>] <pattern>`<br>
`@blacklist/clear[/<list>]`

@blacklist keeps lists of addresses refused at the door, apart from the rules written with @sitelock. Each entry is a sitelock rule for the address, so @sitelock shows it too.

With no switch it counts each list. /add and /del take a dotted (`255.255.255.0`) or prefix (`/24`) mask; without one the entry is the single address. /list shows the addresses, /mask each with its mask, /search those matching a wildcard pattern, and /clear empties the list.

`<list>` is none (may not connect), /noguest (may not log in as a guest) or /register (must register to make a character). The RhostMUSH switches /load and /nodns are refused. You need the players.moderate permission.

```sharp
> @blacklist/add 192.0.2.7
BLACKLIST: 192.0.2.7/32 added to the Blacklist.
> @blacklist
BLACKLIST: Blacklist: 1, NoGuest blacklist: 0, Register blacklist: 0.
```

::: seealso
- [@sitelock]
- [@site]
:::
