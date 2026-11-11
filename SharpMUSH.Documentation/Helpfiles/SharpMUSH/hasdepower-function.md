<!-- help-article
{
  "corpus": "help",
  "id": "hasdepower-function",
  "lookup": "hasdepower()",
  "aliases": [
    "hasdepower",
    "ldepowers()",
    "ldepowers"
  ],
  "sections": []
}
-->
# hasdepower()

`hasdepower(<object>, <depower>)`
`ldepowers(<object>)`

hasdepower() returns 1 when `<object>` holds the RhostMUSH depower `<depower>`, and 0 when it does not. A name RhostMUSH does not have returns `#-1 NO SUCH DEPOWER`. ldepowers() lists the depowers `<object>` holds, separated by spaces.

A depower is a permission taken away here: PAGE is a Deny of `player.page`, BOOT a Deny of the Boot power. An object holds a depower when its own overrides, or its account's, deny every permission that depower stands for. A RhostMUSH import sets those overrides, and `@permission/deny` sets them by hand. A depower SharpMUSH does not import, such as MASTER, is never held. See [roles] for the permissions.

```sharp
> @permission/deny *Twink=player.page
> think hasdepower(*Twink, page)
1
> think ldepowers(*Twink)
PAGE
```

hasdepower() and ldepowers() come from RhostMUSH, where a depower is a level. Here an object holds a depower or does not, and the level is the reach of its Deny (see [@permission reach]).

::: seealso
- [haspower()]
- [permission()]
- [@permission]
:::
