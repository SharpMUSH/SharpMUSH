# WebSocket Support Package — the Channels and Pages Handlers

This document is part of the **WebSocket Support Package**. It describes the
``CHANNEL`MESSAGE``, ``PAGE`MESSAGE``, ``PLAYER`CHANNELS`` and ``CHANNEL`WHO``
event handlers that give a player's web client their channels, pages and who is
on each channel as structured OOB pushes — the data source behind the Play
sidebar's **Channels** and **Pages** groups and the channel view and its member
list (`docs/design/d1/README.md` §5.1, board `06`).
It is the channels-and-pages companion of
[`room-contents-handler.md`](room-contents-handler.md).

> **It ships installed.** The handlers are the bundled **`comm-feed`**
> package (`examples/packages/comm-feed/`), installed at first boot by
> `DefaultPackagesBootstrapService` onto the configured `event_handler` object
> (`{{$event_handler}}`, `#9` by default). Manage it like any other package:
> `@package list`, `@package uninstall comm-feed`, or edit the attributes
> directly (the package's three-way merge keeps local edits on upgrade).

It implements the proposal in §7.3 of the design handoff, which is still
waiting on the product owner's answer to §10 Q4 ("packages as proposed, or
another route?"). The deviations from the proposal are listed at the end.

---

## Who receives what

**The engine decides; the package passes it on.** Every visibility rule
already runs in the engine before a line reaches anyone's terminal, and the
events name the result:

- ``CHANNEL`MESSAGE`` fires once per channel line, **after** it has been
  delivered, with the objids of exactly the members it was delivered to. A
  member who has gagged the channel, one the speaker may not be heard by
  (the `Hear` interaction check), one whose `@chatformat` silenced the line,
  and a muted member's copy of a connect or disconnect line are not among
  them; a `CB_SEEALL` presence line (a hidden player's connect) names only
  See_All members and the speaker. A line nobody received fires nothing.
- ``PAGE`MESSAGE`` fires once per page that reached anyone, naming only the
  recipients it reached: one who is not connected, is HAVEN, refuses pages
  from the pager, or whose page lock the pager fails is not among them.
- ``PLAYER`CHANNELS`` fires for a **connected** player on connect, on resume
  (a web session rebound to its socket, a reload above all), and when
  their channel list may have changed: joining or leaving (including
  `addcom`/`delcom`, `@channel/wipe`, and being joined by someone who controls
  them), a change to their own channel flags (`gag`, `hide`, `mute`,
  `combine`, a title), and the rename or deletion of a channel they are on.
- ``CHANNEL`WHO`` fires when a member comes onto or goes off a channel's
  member list as `@channel/who` lists it (a thing always, a player while
  connected, a member hiding on the channel only to a viewer who may see
  hidden members): on connect, on the last disconnect, on joining or leaving,
  and on starting or stopping `@channel/hide`. It names only the connected
  player members whose view of the list changed, so a hidden member's
  comings and goings reach those who may see them and nobody else, not even
  as a timing.

So `comm.message` goes to the delivered list (plus the pager, for a page, who
saw their own page echoed), `comm.channels` to the player whose list
changed, and `comm.who` to the members the engine named. Nothing reaches a player who did not see the line in their terminal:
the handler can only narrow the engine's list, never widen it.

A hidden speaker is **not** anonymised: `@channel/hide` keeps a member off the
channel's who list, and their lines still name them in every member's
terminal, so they name them in the payload too. An `@cemit` line (with or
without `/spoof`) is the other way round: a member's terminal shows the
message alone — only a NOSPOOF member is told who emitted it — so the event
passes an empty speaker objid and name, and the payload's `from` is empty and
`fromObjid` absent. `%#` is still the emitter.

## Event arguments

| Event | `%0` | `%1` | `%2` | `%3` | `%4` | `%5` | `%6` | `%7` |
|---|---|---|---|---|---|---|---|---|
| ``CHANNEL`MESSAGE`` | channel name | speaker objid (empty when sourceless or an `@cemit`) | style | speaker name (empty for an `@cemit`) | message | recipient objids | unix ms | line id |
| ``PAGE`MESSAGE`` | pager objid | recipient objids | style | pager name, with the page alias when `page_aliases` is on | message | unix ms | page id | |
| ``PLAYER`CHANNELS`` | player objid | cause | channel (empty on connect and resume) | | | | | |
| ``CHANNEL`WHO`` | channel name | member objid | member name | `on` or `off` | viewer objids | cause | | |

- Style is `say`, `pose`, `semipose`, `emit` (`@cemit`) or `presence` (a
  connect or disconnect line) for a channel, and `say`, `pose` or `semipose`
  for a page. The bundled handler sends no `comm.message` for a presence line,
  and the portal's recall endpoint leaves them out: the portal shows who is on
  a channel as its member list instead. (Before `comm-feed` 1.4.0 the handler
  sent them.)
- The name and message are **plain text**, and for a channel, what the
  channel's mogrifier (`MOGRIFY`*`) made of them. A member's own
  `@chatformat` changes only their terminal line.
- ``PLAYER`CHANNELS``' cause is `connect`, `resume`, `join`, `leave`, `status`,
  `rename` or `delete`; ``CHANNEL`WHO``'s is `connect`, `disconnect`, `join`,
  `leave` or `status`.
- The line id is the id the channel's recall buffer holds the line under, which
  the portal's recall endpoint returns too. The page id is the id the page log
  keeps the page under (when `page_log` is on), which the portal's conversation
  recall returns too. Ids are one sequence for every channel line and page,
  taken from the clock in microseconds, so a later line has a larger id, also
  across a restart.
- `%#` is the speaker or pager. For ``PLAYER`CHANNELS`` it is the player on
  connect and resume, and `#1` otherwise: the change is reported by the database write,
  which does not know who asked. For ``CHANNEL`WHO`` it is the member who came or went.

`help event channel`, `help event page` and `help event player` carry the
same in-game.

## The handlers

`me` is the event handler. The manifest
(`examples/packages/comm-feed/package.yaml`) is the source of truth and
comments every attribute; every helper is under ``FN`COMM`` so it cannot
collide with `room-contents`' ``FN`*``.

```mushcode
&CHANNEL`MESSAGE #9=think null(if(strmatch(%2,presence),,oob(u(me/FN`COMM`RECIPIENTS,%5),comm.message,u(me/FN`COMM`MESSAGE,channel,%0,%1,%3,%2,%4,%6,,%7))))
&PAGE`MESSAGE #9=think null(oob(u(me/FN`COMM`RECIPIENTS,setunion(%0,%1)),comm.message,u(me/FN`COMM`MESSAGE,page,,%0,%3,%2,%4,%5,%1,%6)))
&PLAYER`CHANNELS #9=think null(if(u(me/FN`COMM`VIEWER,%0),oob(%0,comm.channels,u(me/FN`COMM`CHANNELS,%0))))
&CHANNEL`WHO #9=think null(oob(u(me/FN`COMM`RECIPIENTS,%4),comm.who,u(me/FN`COMM`WHO,%0,%1,%2,%3)))
```

| Attribute | Decides |
|---|---|
| ``FN`COMM`VIEWER`` (`%0` objid) | who is pushed to: a CONNECTED player |
| ``FN`COMM`RECIPIENTS`` (`%0` objids) | the players a `comm.message` or `comm.who` goes to: ``filter(me/FN`COMM`VIEWER,%0)`` |
| ``FN`COMM`TEXT`` (`%0` style, `%1` name, `%2` message) | `text`: a pose is `Name waves`, a semipose `Name's here`, anything else the message alone |
| ``FN`COMM`CHANNELROW`` (`%0` channel, `%1` viewer) | one row of `channels` |
| ``FN`COMM`CHANNELS`` (`%0` viewer) | the whole `comm.channels` payload |
| ``FN`COMM`MESSAGE`` (`%0` kind … `%7` page recipients, `%8` line id) | the whole `comm.message` payload |
| ``FN`COMM`WHO`` (`%0` channel, `%1` member objid, `%2` member name, `%3` on/off) | the whole `comm.who` payload |

The idioms are the ones `room-contents` uses (see its handler document):
`think null(...)` to swallow `oob()`'s count, `json_array()` with `%r` as the
separator, and a `json_mod()` patch in which `null` removes a key, so a value
a line does not have is absent rather than null. Two more:

- **One `oob()` call per payload.** `oob(<list>, …)` sends the same payload to
  every player in the list. A `comm.message` is the same for everyone who
  received it, so it is built once and sent once; `comm.channels` is one
  player's own list.
- **`cstatus()` takes the object first** on SharpMUSH (`cstatus(<object>,
  <channel>)`), the other way round from PennMUSH.
- **The message is never evaluated again.** `%4` arrives as the text the
  speaker's command produced; `json(string, %4)` escapes it and nothing
  re-parses it, so `\[add(1,2)\]` typed on a channel arrives as `[add(1,2)]`.

## Payloads (v2)

Every payload carries `"v": 2`, lists are sent whole, identity is the objid
(§7.1).

### `comm.channels`

The channels the viewer is **on**, and who the list was built for:

```json
{"v":2,"viewer":{"name":"Ilsa Varn","objid":"#19:1790780895139"},
 "channels":[{"name":"Public","joined":true},{"name":"Builders","joined":true,"gagged":true}]}
```

- `joined` is `true` on every row the package sends. The key is there for a
  game that redefines ``FN`COMM`CHANNELS`` to list channels the viewer could
  join as well, with `"joined": false` — which would then also need a push
  when a channel is created or its see lock changes, and the engine raises no
  event for either.
- `gagged: true` marks a channel the viewer is still on but hears nothing
  from; absent otherwise.
- There is **no `unread`**. The engine has no notion of what a player has
  read — the terminal shows every line — so the portal counts for itself
  (below): from the character's read markers where it has one for the channel,
  which the portal keeps on the server so the count survives a reload, and by
  counting lines as they arrive where it has none. A game that does track it
  can add `"unread": <n>` to a row and the portal takes it.
- `viewer` tells the client whose list it is, which is how it recognises its
  own lines and leaves them out of the unread counts and a conversation's
  "with".

### `comm.message`

A channel line:

```json
{"v":2,"kind":"channel","to":[],"from":"Wren Halloway","text":"anyone up for a scene?",
 "style":"say","ts":1790780182950,"channel":"Public","fromObjid":"#12:1790741467794",
 "id":1790780182950412}
```

A page (here a group pose-page):

```json
{"v":2,"kind":"page","to":["Tomas Reyes","Dace Kellan"],"from":"Ilsa Varn","text":"Ilsa Varn nods",
 "style":"pose","ts":1790780182950,"fromObjid":"#19:1790780895139",
 "toObjids":["#20:1790780895201","#21:1790780895260"],"id":1790780182950977}
```

- `kind` is `channel` or `page`; `channel` appears only on a channel line.
- `to` is the page's recipients as they were reached, in the order paged; it
  is empty for a channel line (a channel has members, not addressees).
  `toObjids` lines up with `to` and appears only on a page.
- `from` is the speaker's name as the terminal line shows it (after the
  mogrifier; with the pager's alias, `Name (alias)`, when `page_aliases` is
  on); it is empty, and `fromObjid` absent, for a line with no speaker and for
  an `@cemit`.
- `text` is the line as the terminal reads it after the channel name or the
  page prefix: a pose carries the name (`Ilsa Varn nods`), because that is how
  a pose reads; `style` says which it was.
- `ts` is milliseconds since 1970, taken when the line was sent.
- `id` is the line's id: for a channel line the one
  `GET api/comm/channels/<channel>/recall` returns for the same line, and for a
  page the one `GET api/comm/conversations/<objids>/recall` returns, so the
  portal keeps one copy of a line it both pulled and was pushed. A page has an
  id whether or not the game keeps a page log. (Before `comm-feed` 1.2.0 a page
  had none.)

### `comm.who`

```json
{"v":2,"channel":"Public","member":{"name":"Wren Halloway","objid":"#12:1790741467794"},"online":true}
```

- `online` is whether the receiving player now lists `member` on `channel`.
  It is a change, not the list: the list itself is
  `GET api/comm/channels/<channel>/who` (`ChannelWhoList`), `@channel/who`'s
  members for the session's character, which the portal reads when it opens
  the channel and keeps current with these.

## What the portal does with them

`SharpMUSH.Client/Services/OobCommFeed.cs` (`ICommFeed`) reads both off the
**play** terminal's OOB store (`CommPayloadParser` does the parsing, tolerant
of a missing `v` and of any malformed member):

- `Channels` is the latest `comm.channels`, each with the feed's own unread
  count.
- A `comm.message` is filed under its channel, or under its conversation: a
  page's key is every participant — pager and recipients, the viewer
  included — by objid where there is one, sorted, prefixed `page `, so every
  page among the same people is one conversation whoever sent it, and it can
  never be a channel name (those cannot hold a space).
- 200 lines are kept per key, or as many as a pull brought back when that
  is more, and the 100 most recent conversations.
- A line from someone else arriving for a key that is not `Viewing` is
  unread until `MarkRead`, unless the key's read marker is already past it
  (markers below). A count a `comm.channels` row carries, 0 included,
  replaces the feed's own; a channel a new list no longer carries is
  forgotten, history and count.
- A new connection or a character switch clears it all, `Viewing` included,
  as it clears the room.
- Once a `comm.channels` says whose feed it is, the feed reads that
  character's read markers (`GET api/comm/markers`) and each channel's recall
  buffer (`GET api/comm/channels/<channel>/recall`), and the channel view pulls
  its channel again when it opens. A channel with a marker is pulled back to it
  (`?lines=200&after=<marker id>`: the last 200 lines, or every line after the
  marker when that reaches further back), so the viewer gets all they missed
  that the buffer still holds; one without a marker takes the whole buffer
  (`?lines=0`). A reconnect logs in again without replaying what was sent
  while the connection was down, so the first `comm.channels` after a drop
  pulls every channel again and lists the conversations again, the same way. A line with an `id` is kept once, however it
  arrived. A channel or conversation with a marker counts as unread only what
  came after it from someone else, so the count survives a reload and a change
  of device; one without a marker counts lines as they arrive. `MarkRead`, and
  a line arriving while `Viewing`, move the marker to the last line
  (`PUT api/comm/markers/channels/<channel>` with the line's id and time;
  `PUT api/comm/markers/conversations` with the others' objids, the page's id
  and its time). A marker never moves back. The endpoints act as the session's
  acting character, and a feed whose viewer is someone else uses none of it.
- When the game keeps a page log (the `page_log` option, a SharpMUSH
  extension, on by default), the feed also lists the character's page
  conversations from it (`GET api/comm/conversations`, the latest 100), so a reload keeps
  them, and pulls those whose last page is past the conversation's marker, back
  to the marker (`?lines=200&after=<marker id>`), so their unread counts
  survive too, and those with no marker, as far back as the server gives
  (`?lines=0`, the latest 500), so pages read on another machine are there.
  Opening a conversation pulls its pages the same way (`GET api/comm/conversations/<objids>/recall`, the others' objids joined
  with spaces). Each character reads only their own copy; there is no staff
  read. With `page_log` off both answer `"logging": false` and nothing else,
  and the conversation view says the game keeps no page history. The feed
  then drops what it pulled from the log (pages pushed live stay, and a
  conversation known only from the listing goes). Opening a conversation asks
  again, since the option can be turned on at any time. Conversations are ordered,
  and the least recent dropped past 100, by their latest page's id. A
  conversation with more than 32 other people is not marked. As
  for channels, a failed read of the markers lists and pulls no conversation,
  and a failed listing, or one answered while `page_log` was off, is retried
  on the next `comm.channels`. Lines with ids are kept in id order, since ids
  keep rising when the clock steps back.
- The recall endpoint refuses what `@channel/recall` refuses: a channel the
  character may not see answers 404, as a missing one does, and one they are
  not on and could not join answers 403. A line only See_All members were sent
  stays hidden from everyone else. Its `text` goes through the installed
  ``FN`COMM`TEXT`` on the handler, with the handler as executor, the speaker as
  enactor and the same arguments as the push, so a game that redefines it gets
  the same text pulled as pushed. The bundled default is used only when the
  attribute is absent. A logged page's `text` is composed the same way, with
  the pager as enactor.

`SharpMUSH.Client/Services/ChannelWhoFeed.cs` (`IChannelWho`) keeps the member
lists: it reads a channel's list from `api/comm/channels/<channel>/who` when the
channel view opens it, applies each `comm.who` to every list it holds, and reads
the open channel's list again on every `comm.channels` (connect, resume, a
change to the viewer's own channels), since a change made while the connection
was down is not replayed.

## Testing

`SharpMUSH.Tests.Integration/Packages/CommFeedPackageTests.cs` drives the real
path: players on websocket connections, the command they would type, the event,
the package as installed at boot, and `oob()` publishing to NATS. It reads
what each connection was sent off the NATS subject the connection server
consumes, up to a probe each watched player sends itself afterwards, so
"received nothing" is observed rather than timed out. It covers a channel
member against a non-member, a gagged member, a hidden speaker, a pose,
`@cemit` and `@cemit/spoof`, a hidden member's connect line (See_All only, and
not to a member who muted the channel), a page
to one and to several, a page lock and a HAVEN refusing it, a group page some
recipients refuse, joining, gagging and leaving, a rename and a deletion, and
connecting. It also checks that a pushed line's or page's `id` is the one the
recall endpoints return, and that recalled text goes through the installed
``FN`COMM`TEXT``.

`SharpMUSH.Tests/Services/ChannelBroadcastServiceTests.cs` checks the
``CHANNEL`MESSAGE`` arguments and that an undelivered line raises nothing;
`SharpMUSH.Tests/Commands/PageLogCommandTests.cs` that a page is logged for its
sender and the recipients it reached only while `page_log` is on;
`SharpMUSH.Tests.Integration/Portal/PageLogApiTests.cs` the conversation
endpoints, as mortals and as a wizard who still reads only their own;
`SharpMUSH.Tests.BUnit/Services/CommPayloadParserTests.cs`,
`OobCommFeedTests.cs`, `OobCommFeedHistoryTests.cs` and
`OobCommFeedPageLogTests.cs` cover the client.

## Differences from §7.3

- **No unread counts in `comm.channels`.** The proposal has the list carry
  them; the engine does not know them. The portal keeps per-character read
  markers on the server instead (`api/comm/markers`) and counts from those.
- **`to` is empty on a channel line** rather than naming the channel, which is
  in `channel`.
- **Added keys:** `viewer` on `comm.channels`, `gagged` on a row, and
  `style` and `toObjids` on `comm.message` — the first so the client knows
  whose list and lines these are, the last so a conversation is keyed by
  identity rather than by names that can change.
- **Pushed on change, on connect and on resume**, where the proposal says "on
  change": a fresh connection, or a reloaded page, has nothing until then.
