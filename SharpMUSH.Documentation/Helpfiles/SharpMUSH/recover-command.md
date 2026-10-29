<!-- help-article
{
  "corpus": "help",
  "id": "recover-command",
  "lookup": "@recover",
  "aliases": [
    "RECOVER LIST"
  ],
  "sections": [
    {"id": "listing", "heading": "Listing the recover list", "lookup": "@recover listing"},
    {"id": "recovering", "heading": "What a recovery puts back", "lookup": "@recover recovering"},
    {"id": "keeping", "heading": "Keeping and purging entries", "lookup": "@recover keeping"}
  ]
}
-->
# @recover

`@recover[/list] [<pattern>]`<br>
`@recover/type|/owner|/age <value>`<br>
`@recover/count [<pattern>]`<br>
`@recover/detail #<number>`<br>
`@recover <object>|#<number>`<br>
`@recover/exits <room>`<br>
`@recover/keep #<number>[=<reason>]`<br>
`@recover/unkeep #<number>`

A destroyed object is not deleted outright. When the purge frees it, it goes onto the recover list, still holding its number, its attributes and what it pointed at. No new object gets that number while the entry is there. `@recover` brings it back under the same number, so references to it, locks naming it and snapshots of it find it again.

Given an object still marked GOING, `@recover` spares it, as `@undestroy` does. RhostMUSH code that types `@reclist` gets `@recover`.

- [@recover listing]: who sees which entries, and the filters
- [@recover recovering]: what comes back and what falls back
- [@recover keeping]: keep marks, how long entries stay, and `@purge`

## Listing the recover list

`@recover` with no object lists the entries you can see: those for objects you owned, or every entry if you have the See_All power. Each row shows the number, name, type, former owner, the day it was destroyed and the day it expires. `/list <pattern>` matches names with `*` and `?`, `/type` takes PLAYER, ROOM, THING or EXIT, `/owner` a player, and `/age <days>` shows entries destroyed at least that many days ago. `/count` gives the number only.

`@recover/detail #<number>` shows one entry in full, and what a recovery of it would do.

Example:
```
> @recover/type thing
```

## What a recovery puts back

You may recover an entry if you control its former owner, or if you are a wizard. The object comes back with its name, flags, powers, roles, attributes, parent, zone and home, set HALT until you look it over. A character is linked to its account again if the account still exists.

When something it pointed at is gone too, the recovery falls back and says so: a thing goes to your inventory, a character to default_home, a missing parent or zone is left unset. An exit needs its room; recover the room, then `@recover/exits <room>` brings back the exits destroyed with it.

It does not undo what happened around the destruction: what it owned stays with the probate player, contents stay where they were sent, and deleted mail stays deleted.

## Keeping and purging entries

Entries expire after recover_keep_players for characters (forever by default) and recover_keep_objects for everything else (30 days by default); see [@config dump].

`@recover/keep #<number>=<reason>` marks an entry kept: it never expires and a bulk `@purge` leaves it alone. `/unkeep` clears the mark. An object a package installed is kept the same way while the package is installed, so a rollback finds it.

`@purge #<number>` removes one entry for good and frees its number; a kept one needs `@purge/confirm #<number>`. See [@purge] for removing many at once.

::: seealso
- [@destroy]
- [@undestroy]
- [@purge]
- [object snapshots]
:::
