<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-events",
  "lookup": "compatibility events",
  "aliases": [],
  "sections": [
    {
      "id": "pennmush-events-compatibility-events",
      "heading": "PennMUSH events: `compatibility_events`",
      "lookup": "compatibility events compatibility_events"
    }
  ]
}
-->
# Compatibility Events

Some PennMUSH events report on things a SharpMUSH game does not do on its own. The compatibility_events option makes it do them, so an event handler written for PennMUSH keeps working. [event list] lists every event.

## PennMUSH events: `compatibility_events`

**A choice.** Off by default; importing a PennMUSH database turns it on.

**PennMUSH** saves its database every dump_interval, checks it every dbck_interval, disconnects players idle past idle_timeout, and keeps one connection to its SQL server. It raises the dump events, db\`dbck, player\`inactivity and the sql events around those, log\`huh for a command it did not understand, signal\`usr1 and signal\`usr2 for user signals, and http\`fail for an HTTP request it cannot read.<br>
**SharpMUSH**, with compatibility_events off, raises none of them. It writes every change to disk as it happens, keeps its links consistent as it writes them, leaves idle players connected, and leaves user signals alone. With compatibility_events on, it runs PennMUSH's save, check and inactivity timers, with their messages, treats user signals as PennMUSH does, and raises each event as PennMUSH does.<br>
**Why.** A new game has no softcode waiting on those events, and the save broadcasts and idle disconnects would only be noise in it. An imported game keeps them, so its event handler runs unchanged.<br>
**Workaround.** Turn the option on with `@config/set compatibility_events=yes`.

```sharp
> @dump
Dump command does nothing for SharpMUSH. Consider using @backup.
> think config(compatibility_events)
No
```

With the option on, `@dump` saves the world at once and raises the dump events, as PennMUSH's does:

```sharp compatibility_events
> @dump
Dumping...
GAME: Saving database. Game may freeze for a few moments.
GAME: Save complete.
Dump complete.
> think config(compatibility_events)
Yes
```

dump_message, dump_complete, dump_warning_5min and dump_warning_1min set the messages; an empty one is not shown. [event dump], [event db], [event log], [event sql], [event signal], [event player] and [event http] describe each event.
