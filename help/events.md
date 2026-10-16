<!-- help-article
{
  "corpus": "help",
  "id": "events",
  "lookup": "Events",
  "aliases": ["Event"],
  "sections": [
    {"id": "handler-code", "heading": "How handler code runs", "lookup": "events handler code"},
    {"id": "several-attributes", "heading": "Several attributes on one event", "lookup": "events several attributes"}
  ]
}
-->
# Events

SharpMUSH Events are hardcoded events that may or may not be caused by players. An object designated as the event handler (via the "event_handler" config option) has attributes triggered on it, with arguments, on specified events.

Unlike PennMUSH, **SharpMUSH pre-populates the event handler**: a new database is seeded with an **Event Handler object (#9)**, and the "event_handler" config option already points at it. You do not create one or set the config; you simply add attributes named after the events you care about:

```sharp
> &<event name> #9=<action list>
```

If you would rather use a different object, point the config at it with `@config/set event_handler=<dbref>` and set the "event_handler" option in your mush.cnf so it survives dumps and is receiving events even during startup. On a fresh instance this is optional.

- [events handler code]: permissions, queueing and the enactor
- [events several attributes]: package attributes under one event

## How handler code runs

- Event attributes run with the handler object's **own permissions** (it executes as itself, like the HTTP handler and any normal attribute). The seeded Event Handler **#9 is a WIZARD object**, so out of the box it can `@set`, `@power`, `@lock`, and see-all as an admin handler needs. If you point event_handler at your own object, flag it wizard (`@set <obj>=wizard`) to grant it those powers.
- An event is **queued**, as in PennMUSH: the attribute becomes a queue entry of its own, run by the handler and counted against the handler's queue. The command that caused the event does not wait for it, and the event gets its own "queue_entry_cpu_time" limit rather than sharing the causer's. A connecting player's auto-look, for instance, is shown before PLAYER\`CONNECT runs.
- The enactor (%#) is the executor that caused the event. For a **system event with no executor** (a scheduled check, a connection at the connect screen), %# is **#1 (God)**, not #-1. If the causer has been destroyed since, %# is #1 as well. Because %# is a real dbref either way, use an event's own arguments (not %#) to distinguish system- from player-caused triggers.

## Several attributes on one event

The handler runs the attribute named after the event, then each attribute one level below it, in name order. A package adds its own code to an event as one of those, named after itself, and never edits the event's own attribute or another package's:

```sharp
> &PAGE`MESSAGE`TALLY #9=&PAGES me=[add(get(me/PAGES),1)]
> page Bob=Hi.
You paged Bob with 'Hi.'
> think get(#9/PAGES)
1
```

With the comm-feed package installed, a page now runs PAGE\`MESSAGE (if it has a value), then PAGE\`MESSAGE\`COMMFEED, then PAGE\`MESSAGE\`TALLY. Each gets the event's arguments by position and by name. An empty attribute runs nothing, so ``&PAGE`MESSAGE`COMMFEED #9=`` silences one package and leaves the others. Only one level is run: PAGE\`MESSAGE\`SCENE\`LOG is not, so a package keeps its helpers under its own attribute.

- On most events each attribute is queued as an entry of its own, as `@trigger` would queue it: one that fails or runs out of time does not affect the others.
- A **stoppable** event runs them in turn in one queue entry instead, as `@include` would, so one can call [stopevent()] to keep the ones after it from running. [event list] marks the stoppable events.

::: seealso
- [EVENT LIST]
- [event examples]
- [stopevent()]
:::
