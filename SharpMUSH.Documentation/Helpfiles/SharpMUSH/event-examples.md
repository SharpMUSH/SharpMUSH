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
> &SOCKET`LOGINFAIL #9=@wizwall/emit On descriptor '%0' from IP '%1' a failed connect attempt to '%4': '%3'
(Later, a player attempts to log in as #1)
Broadcast: [Event Handler]: On descriptor 3, from IP '127.0.0.1', a failed connect attempt to '#1': 'invalid password'
```


::: seealso
- [event examples player creation example]
:::

## Player creation example

Suppose you want `@pcreated` players to be powered builder, set shared and zonelocked to roys, but players created at the connect screen to not be. Set the handler on the seeded Event Handler (#9). Distinguish the two cases with the event's *how* argument (%2, one of pcreate, create, register), **not** %#: for a connect-screen create %# is #1 (God), so `@assert %#` would not skip it.
```sharp
> &PLAYER`CREATE #9=@assert strmatch(%2,pcreate) ; @pemit %#=Auto-Setting [name(%0)] Builder and shared ; @power %0=builder ; @lock/zone %0=FLAG^ROYALTY ; @set %0=shared
> @pcreate Grid-BC
Auto-Setting Grid-BC Builder and Shared
```
Note there is no `@set #9=wizard` step: the seeded #9 is already a wizard object, so it runs with its own elevated permissions and can `@power`/`@lock` the new player as-is. (A custom, non-wizard handler object would need `@set <obj>=wizard` first.)

The Event Handler object, since it's handling so many events, may become cluttered with attributes. We recommend using `@trigger` and `@include` to separate events to multiple objects.
