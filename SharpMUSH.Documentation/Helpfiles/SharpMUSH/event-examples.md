<!-- help-article
{
  "corpus": "help",
  "id": "event-examples",
  "lookup": "event examples",
  "aliases": [],
  "sections": [
    {
      "id": "player-creation-example",
      "heading": "Player creation example",
      "lookup": "event examples player creation example"
    },
    {
      "id": "discord-presence-example",
      "heading": "Discord presence example",
      "lookup": "event examples discord presence example"
    }
  ],
  "redirects": {
    "EVENT EXAMPLES2": "event examples player creation example"
  }
}
-->
# Event Examples

Suppose you want random dbsave messages:
```sharp
> &DUMP`COMPLETE #9=@config/set dump_complete=SAVE: [v(randword(lattr(me/dumpmsg`*)))]
> &DUMPMSG`NOTHING #9=The Database has been saved, nothing to see here.
> &DUMPMSG`GRETZKY #9=The Database saves, but Gretzky scores!
> &DUMPMSG`GEICO #9=The Database saved 15% by switching to Geico!
> @dump
SAVE: The Database has been saved, nothing to see here.
> @dump
SAVE: The Database saved 15% by switching to Geico!
```

Or admin want to be notified when a player connect attempt fails:
```sharp
> &SOCKET`LOGINFAIL #9=@wizwall/emit On descriptor '%<descriptor>' from IP '%<ip>' a failed connect attempt to '%<objid>': '%<reason>'
(Later, a player attempts to log in as #1)
Broadcast: [Event Handler]: On descriptor 3, from IP '127.0.0.1', a failed connect attempt to '#1': 'invalid password'
```


::: seealso
- [event examples player creation example]
- [event examples discord presence example]
:::

## Player creation example

Suppose you want `@pcreated` players to be powered builder, set shared and zonelocked to roys, but players created at the connect screen to not be. Set the handler on the seeded Event Handler (#9). Distinguish the two cases with the event's *how* argument (`%<how>`, one of pcreate, create, register), **not** %#: for a connect-screen create %# is #1 (God), so `@assert %#` would not skip it.
```sharp
> &PLAYER`CREATE #9=@assert strmatch(%<how>,pcreate) ; @pemit %#=Auto-Setting [name(%<objid>)] Builder and shared ; @power %<objid>=builder ; @lock/zone %<objid>=FLAG^ROYALTY ; @set %<objid>=shared
> @pcreate Grid-BC
Auto-Setting Grid-BC Builder and Shared
```
Note there is no `@set #9=wizard` step: the seeded #9 is already a wizard object, so it runs with its own elevated permissions and can `@power`/`@lock` the new player as-is. (A custom, non-wizard handler object would need `@set <obj>=wizard` first.)

The Event Handler object, since it's handling so many events, may become cluttered with attributes. We recommend using `@trigger` and `@include` to separate events to multiple objects.

## Discord presence example

Mudlet can show your game in its players' Discord status. When it connects it sends the GMCP package External.Discord.Hello, and a game that answers with External.Discord.Info gets an invite link shown there. Answer from socket\`gmcp, sending to the descriptor (`%<descriptor>`), since nobody has logged in yet:
```sharp
> &SOCKET`GMCP #9=@assert strmatch(%<package>,External.Discord.Hello) ; think oob(%<descriptor>,External.Discord.Info,json(object,inviteurl,json(string,https://discord.gg/example)))
```
Then say what each player is doing once they log in, from player\`connect:
```sharp
> &PLAYER`CONNECT #9=think oob(%<descriptor>,External.Discord.Status,json(object,details,json(string,Playing [name(%<objid>)])))
```
If #9 already has a PLAYER\`CONNECT, add the oob() to it rather than replacing it. Nothing is sent to a client that did not agree to GMCP, so both are safe on every connection.
