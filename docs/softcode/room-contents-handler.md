# WebSocket Support Package — the ``ROOM`CONTENTS`` Handler

This document is part of the **WebSocket Support Package**. It describes the
``ROOM`CONTENTS`` event handler that fans out structured OOB pushes to a room's
connected occupants whenever the room's population changes (player movement,
connect, or disconnect), and to a resumed session — the data source behind the
portal's Play page.

> **It ships installed.** The handler is the bundled **`room-contents`**
> package (`examples/packages/room-contents/`), installed at first boot by
> `DefaultPackagesBootstrapService` onto the configured `event_handler` object
> (`{{$event_handler}}`, `#9` by default). Nothing below needs typing on a
> stock game — it is here to explain what is installed and how to change it.
> Manage it like any other package: `@package list`, `@package uninstall
> room-contents`, or edit the attributes directly (the package's three-way
> merge keeps local edits on the next upgrade).

Version 2.x of the package speaks **OOB v2** (`docs/design/d1/README.md` §7.1,
`docs/superpowers/specs/2026-09-29-image-attributes-and-oob-v2-design.md` §4).
The v1 shapes are at the end; v2 only adds keys to them.

---

## What the handler does

When ``ROOM`CONTENTS`` fires the handler receives:

| Register | Value |
|----------|-------|
| `%0` | Dbref of the affected room |
| `%1` | Cause: `move-in`, `move-out`, `connect`, `disconnect`, or `resume` |
| `%#` | The object that **caused** the event — see below |

It does the viewer-independent work once (``FN`PREPARE``), then for **each**
connected player in the room builds that player's own view and sends it over
their WebSocket (or GMCP) connection:

- **`room.contents`** — `{"v": 2, "who": [ … ]}`, one row per non-exit
  occupant the viewer may see (things and players).
- **`room.exits`** — `{"v": 2, "exits": [ … ]}`, one row per exit the viewer
  may see, with the `goto` command a client issues to traverse it.
- **`room.info`** — `{"v": 2, …}`, the room itself: identity, area, picture,
  description, scene. Sent on `move-in`, `connect` and `resume` only.

**`resume`** is a web session the connection server rebound to its socket, still
logged in — a page reload above all. The page holds none of the state pushed
before, and the server's replay covers only the frames after its `lastSeq`, so
the engine fires the event as on connect (`docs/design/d1/README.md` §7.1).
Nothing in the room changed for anyone else: the viewers are the enactor alone
(the resuming player, when they are a viewer in the room), and they get all
three packages.

What depends on who is looking, which is why one JSON for the whole room
(what 1.0 did) is not enough: a DARK occupant or exit is omitted for a viewer
who may not see it (the handler is a wizard object and `lcon()` shows it
everything); an exit's `state` is `locked` when its lock fails *for that
viewer*, and only then does it carry a `hint` — and only when it is *not*
locked does it carry the destination preview; the viewer's own row says
`"you": true`; the `scene` block appears only for a viewer who may see the
scene.

**`%#` is not the mover.** The event's enactor is whoever caused the change:
the player who walked or connected, but also the wizard who `@tel`'d someone
else, or God (`#1`) for a system move such as a void rescue, or a thing that
moved itself. The handler sends `room.info` to the enactor alone when the
enactor is a viewer in the room (the usual case), and to every connected
occupant otherwise; the mover is then unknown or receives nothing, and a client already holding the same `room.info` can ignore a
repeat. A ``ROOM`INFO`` engine event that would fire on a name, image or
description edit is deferred; the engine has no attribute-change event, and a
client that wants a fresh `room.info` outside these causes has no route to it
yet (the terminal's `query.*` evaluates as the *player*, so
``u(#9/FN`PAYLOAD`INFO,…)`` from there runs with `me` = the player and does
not work).

**Descriptions and the enactor.** ``FN`DESC`` evaluates a room's `DESCRIBE` as
the room (the executor `look` gives it) with the event's enactor as `%#`/`%N`,
**once per event**, and the text is pushed to every viewer that gets it. A
description that branches on who is looking (`hasflag(%#,WIZARD)`, `%N`)
therefore shows one branch to everyone; limiting `room.info` to the enactor
covers the common case, but destination previews go to everyone. A game whose
descriptions branch on the viewer blanks ``FN`DESC``.

**Cost.** With K occupants, M exits and N viewers, one event is O(K+M)
evaluations of descriptions and images (once, in ``FN`PREPARE``) plus
O(N·(K+M)) cheap per-viewer patches, of which one `elock()` per exit per
viewer is the largest. ``FN`DESC`` is the expensive helper — a `DESCRIBE`
evaluation for the room and for each exit destination. On a big hub, blank it
(``&FN`DESC #9=``) and the page falls back to its own text; blank ``FN`DEST``
to drop destination previews entirely.

---

## Idioms used (and the traps they avoid)

These were learned the hard way; the handler relies on all of them:

1. **`think null(...)` — evaluate for effect, print nothing.** `oob()` is a
   function, so the handler is one `think`; `null()` evaluates its arguments
   and swallows the delivery counts that would otherwise be thought at the
   handler object.

2. **`oob(<target>, <package>, <json>)` is called once per viewer.** It takes
   a target list and delivers only to players holding a live WebSocket (or
   GMCP) connection — but the *payload* is the same for every target in one
   call, so per-viewer content means per-viewer calls: `iter()` over
   ``filter(me/FN`VIEWER, lcon(%0))`` with `%i0` as the viewer.

3. **Hoist with q-registers.** `u()` shares the caller's registers on this
   engine (only `localize()`/`ulocal()` push a frame), so ``FN`PREPARE`` can
   `setq()` every base row once — `w<n>` per occupant, `x<n>`/`d<n>` per exit,
   `info<n>` for the room — and the per-viewer helpers read them back with
   `r()`. Each helper falls back to building its base on the spot
   (`strfirstof(r(...), u(...))`) so it also works called alone.

4. **`lcon()`, `lexits()`, `loc()` and `filter()` answer objids**, as
   `#12:1790741308439`, not bare dbrefs. A register name cannot hold the
   colon, and `member(lcon(%0),%#)` never matches a bare `%#`: every key and
   every comparison goes through `num()`.

5. **Build JSON arrays with `json_array()`, not `json(array, iter(...))`.**
   `json(array, …)` takes each element as a *separate* argument, so feeding it
   a single `iter()` list cannot work. `json_array(<list>[, <delim>])`
   assembles a list of already-formed JSON values into an array, and answers
   `[]` for an empty list. Produce the per-element JSON with `iter()`.

6. **Use `%r` as the `json_array`/`iter` separator.** A row embeds names,
   descriptions and hints, which can contain a space or a `|`; it cannot
   contain a raw newline once `json(string, …)` has escaped it. 1.0 used `|`,
   which a description can contain.

7. **Optional keys are a `json_mod(<base>, patch, <patch>)`.** Function
   arguments are split before evaluation, so a key/value pair cannot be
   conditionally *inserted* into `json(object, …)`. Instead every row is a
   base object plus a merge patch (RFC 7396): a `null` value in the patch
   **removes** the key. ``FN`IMAGE`` answers the word `null` for an object
   with no `IMAGE`, and the row comes out without an `image` key at all.

   **Publish only a visual image attribute.** The handler is a wizard, so
   `get()` reads an attribute its owner made private. The seeded `visual`
   flag is a default for a *new* attribute: an `IMAGE` set before the seed
   existed, or with `visual` cleared since, keeps its owner's choice.
   ``FN`PICTURE`` reads an image attribute only when it is `visual`, and a
   leaf such as `` IMAGE`BANNER `` only when `IMAGE` is `visual` too: the flag
   does not propagate down a tree, and a private branch hides its leaves.

8. **Validate before `json(number, …)`.** One malformed value — an
   `` IMAGE`FOCAL `` of `center` — made `json(number,…)` an error, the array
   `#-1`, the patch invalid, the row not JSON, and then `oob()` rejected the
   whole `room.contents` for every viewer, silently. ``FN`FOCALOK`` checks the
   shape first and the key is dropped instead.

9. **A regex in softcode needs `\[` and `\]`**, because a bare `[...]` is
   evaluated as a function call and its brackets vanish; a bare `)` inside an
   argument closes the call, so groups are out too. ``FN`COLOR``'s
   `^#\[0-9a-fA-F\]…$` is spelt out six times for that reason, and
   ``FN`FOCALOK`` uses no regex at all.

10. **Filter with a stored attribute, not `#lambda`.** ``filter(me/FN`WHOVIS,
    lcon(%0),,,%1)`` is clean; the `#lambda/...` inline form mis-splits on
    commas inside the lambda body. `filter()` passes its arguments from the
    fifth on to the predicate as `%1…`, which is how the viewer gets there.

11. **`lcon(%0)` includes exits in this engine.** Filter them out of the *who*
    list with `not(hastype(%0,exit))` so exits don't appear as occupants.

12. **Connected detection.** `hasflag(%0,CONNECTED)` reflects presence: true
    for a player with a live *play* session, false for a disconnected or
    portal-only (background) connection. There is no `isplayer()` on this
    engine (nor on PennMUSH) — 1.0 called one, and `not()` of the resulting
    error was true, so every disconnected player was listed. Use
    `hastype(%0,player)`.

13. **An unknown function is literal text**, as on PennMUSH: without the Scene
    plugin, `scenewhere(#12)` evaluates to `scenewhere(#12)`, not to an error.
    And with the plugin, `scenewhere()` of a scene-less room is the constant
    `#-1 NOT FOUND`, which `scene(#-1 NOT FOUND, id)` echoes back. ``FN`SCENE``
    rejects a `#-1*` answer first, then asks `scene(<id>, id)` to echo the id.

14. **`objeval(<obj>, <expr>)` evaluates the object argument** (fixed
    alongside 2.0 to match `fun_objeval`): ``FN`DESC`` runs a room's
    `DESCRIBE` as the room, so its softcode runs with the room's permissions
    and not the wizard handler's.

15. **`num(%0)` returns the `#N` dbref form** (e.g. `#76`), so don't prepend
    an extra `#`. Side-effect builders (`dig()`, `open()`, `create()`) answer
    an **objid**; `num()` it before comparing.

---

## The handler

These are the attributes the `room-contents` package installs on the event
handler object. `me` is the event handler itself, since a handler runs with
the handler object as its executor. Every example targets `#9`, the default
`event_handler`; the package resolves `{{$event_handler}}` from config at
install time, so on a game that configures a different handler object,
substitute its dbref throughout. The manifest
(`examples/packages/room-contents/package.yaml`) is the source of truth and
carries a comment per attribute; this is the map.

**Predicates.** `%0` is the subject, `%1` (where taken) the viewer.

| Attribute | Answers 1 when |
|---|---|
| ``FN`WHOVIS`` (`%0`, `%1`) | the occupant is listed to the viewer: not an exit; a player only if CONNECTED; a DARK one only to Wizard/Royalty, `See_All`, or itself |
| ``FN`VIEWER`` (`%0`) | the occupant receives pushes: a CONNECTED player |
| ``FN`ONLINE`` (`%0`) | a player counts toward a destination's `here`: CONNECTED and not DARK |
| ``FN`EXITVIS`` (`%0`, `%1`) | the viewer may see the exit: not DARK, or LIGHT, or the viewer is Wizard/Royalty/`See_All` |
| ``FN`FOCALOK`` (`%0` value) | an `` IMAGE`FOCAL `` is two numbers, each 0 to 1 |

**Value helpers.**

| Attribute | Gives |
|---|---|
| ``FN`STATUS`` (`%0` idle seconds) | `active` under 5 min, `idle` under 30, else `away` |
| ``FN`AREA`` (`%0` room) | the name of the room's zone, else its parent, else nothing |
| ``FN`COLOR`` (`%0` player) | `` PROFILE`COLOR `` as a JSON string when it is `#rrggbb`, else `null` |
| ``FN`PICTURE`` (`%0` object, `%1` attribute) | the attribute's value when it and its branch are `visual`, else blank |
| ``FN`IMAGEREF`` (`%0` object, `%1` URL) | `{"url","alt","focal"}` with alt/focal from ``IMAGE`ALT``/``IMAGE`FOCAL``, or `null` for a blank URL |
| ``FN`IMAGE`` (`%0` object) | the thumbnail: ``FN`IMAGEREF`` of `IMAGE` |
| ``FN`BANNER`` (`%0` room) | the banner: ``FN`IMAGEREF`` of ``IMAGE`BANNER``, falling back to `IMAGE` |
| ``FN`DESC`` (`%0` room) | the room's `DESCRIBE`, evaluated as the room |
| ``FN`SCENE`` (`%0` room, `%1` viewer) | `{"id","title","cast"}` for a scene the viewer may see, or `null` |
| ``FN`EXITHINT`` (`%0` exit) | the exit's `@fail`, as stored — plain text, never evaluated |
| ``FN`EXITSTATE`` (`%0` exit, `%1` viewer, `%2` loc(exit)) | `closed` when unlinked (`#-1`), `locked` when the Basic lock fails for the viewer, else `open` (VARIABLE `#-2` and HOME `#-3` stay open) |
| ``FN`DEST`` (`%0` destination) | `{"name","area","image","desc","here"}` |

**Base rows, built once per event.**

| Attribute | Gives |
|---|---|
| ``FN`WHOBASE`` (`%0` occupant) | dbref, objid, type, name, cmd; for players image, color, status, idle, profile |
| ``FN`EXITBASE`` (`%0` exit) | dbref, objid, name, aliases, cmd, confirm |
| ``FN`EXITDEST`` (`%0` exit) | the ``FN`DEST`` of where it leads, or nothing |
| ``FN`INFOBASE`` (`%0` room) | `room.info` without the scene block; its image is ``FN`BANNER`` |
| ``FN`PREPARE`` (`%0` room) | sets `mover`, `w<n>`, `x<n>`, `d<n>`, `info<n>` |

**Rows and payloads, per viewer.**

```mushcode
&FN`WHOROW #9=json_mod(strfirstof(r(w[rest(num(%0),#)]),u(me/FN`WHOBASE,%0)),patch,json(object,you,if(strmatch(num(%0),num(%1)),true,null),actions,if(cand(hastype(%0,player),not(strmatch(num(%0),num(%1)))),json(array,json(object,label,json(string,Page),cmd,json(string,page [num(%0)]=))),null)))
&FN`EXITROW #9=json_mod(strfirstof(r(x[rest(num(%0),#)]),u(me/FN`EXITBASE,%0)),patch,json(object,state,json(string,setr(st,u(me/FN`EXITSTATE,%0,%1,loc(%0)))),hint,if(cand(strmatch(%q<st>,locked),setr(h,u(me/FN`EXITHINT,%0))),json(string,%q<h>),null),dest,if(cand(not(strmatch(%q<st>,locked)),setr(dd,strfirstof(r(d[rest(num(%0),#)]),u(me/FN`EXITDEST,%0)))),%q<dd>,null)))
&FN`PAYLOAD`CONTENTS #9=json(object,v,json(number,2),who,json_array(iter(filter(me/FN`WHOVIS,lcon(%0),,,%1),u(me/FN`WHOROW,%i0,%1),,%r),%r))
&FN`PAYLOAD`EXITS #9=json(object,v,json(number,2),exits,json_array(iter(if(hastype(%0,room),filter(me/FN`EXITVIS,lexits(%0),,,%1)),u(me/FN`EXITROW,%i0,%1),,%r),%r))
&FN`PAYLOAD`INFO #9=json_mod(strfirstof(r(info[rest(num(%0),#)]),u(me/FN`INFOBASE,%0)),patch,json(object,scene,u(me/FN`SCENE,%0,%1)))
&ROOM`CONTENTS #9=think null(u(me/FN`PREPARE,%0),iter(if(strmatch(%1,resume),%q<mover>,filter(me/FN`VIEWER,lcon(%0))),[oob(%i0,room.contents,u(me/FN`PAYLOAD`CONTENTS,%0,%i0))][oob(%i0,room.exits,u(me/FN`PAYLOAD`EXITS,%0,%i0))][if(cand(match(move-in connect resume,%1),cor(not(%q<mover>),strmatch(num(%i0),%q<mover>))),oob(%i0,room.info,u(me/FN`PAYLOAD`INFO,%0,%i0)))]))
```

Reading the main handler:

- ``u(me/FN`PREPARE, %0)`` — once: every base row and the room's info into
  registers; `mover` = the enactor's dbref if the enactor is a viewer
  (``FN`VIEWER``) in the room.
- ``filter(me/FN`VIEWER, lcon(%0))`` — the viewers: connected players in the room;
  for `resume`, `%q<mover>` alone (the resuming player, or nobody).
- `iter(<viewers>, …)` — for each, with `%i0` the viewer:
  - ``oob(%i0, room.contents, u(me/FN`PAYLOAD`CONTENTS, %0, %i0))`` — the who
    list as this viewer sees it, to this viewer alone;
  - the same for `room.exits`;
  - on the arrival causes, `room.info` — to the mover when known, else to all.
- The exits payload is guarded on `%0` being a room. The event fires for
  whichever container was affected and that is not always a room — a player
  carrying something, or a thing something was teleported into, raise it too.
  `lexits()` answers `#-1` for those, as PennMUSH does, and an unguarded
  `iter()` would build a row out of `#-1`. An empty exits list is the right
  answer for a non-room, and still worth sending: it clears whatever the
  sidebar showed before.

Verify it is set:

```text
> get #9/ROOM`CONTENTS
```

### Data the rows read

All of it as **plain text** via `get()`, never evaluated:

- `IMAGE`, ``IMAGE`ALT``, ``IMAGE`FOCAL`` on any object (`help IMAGE`) — the picture.
- ``PROFILE`COLOR`` on a player — the row's `color`, only as `#rrggbb`.
- `CONFIRM` on an exit — a confirmation the client shows before traversing.
- `@fail` on an exit — the `hint` on a locked row; softcode in it arrives as the code itself.
- A room's zone or `@parent` — its `area`.

---

## Silencing the handler

```text
&ROOM`CONTENTS #9=
```

An empty `&` sets the attribute to an empty string, which silences the handler
(the engine still fires the event, but the attribute executes nothing). To
remove it properly — attributes and registry records together — uninstall the
package instead:

```text
> @package uninstall room-contents
```

---

## Testing the handler

`SharpMUSH.Tests/Services/RoomContentsHandlerReferenceTests.cs` triggers
``ROOM`CONTENTS`` via the **real event path** —
`EventService.TriggerEventAsync(parser, SharpEvents.RoomContents, enactor, room, cause)` —
not `@trigger`. The event path runs the attribute with the event handler object
as its executor (`me`, `%!`, `%@`) and the causing object as `%#`; seeded `#9`
is a WIZARD, which is what lets the handler's introspection calls see the room.

The v2 tests install the package's own attributes from the embedded manifest
and substitute only the handler, with one that runs ``FN`PREPARE`` and then
stores what a payload builder answers for a named viewer —
``&LAST_PAYLOAD #9=[null(u(me/FN`PREPARE,%0))][u(me/FN`PAYLOAD`EXITS,%0,#13)]``
— instead of sending it, since `oob()` needs a live WebSocket the harness
lacks. The room they build is deliberately hostile: a DARK wizard with a valid
`` PROFILE`COLOR ``, a mortal with a portrait, a bad `` IMAGE`FOCAL `` and a
`` PROFILE`COLOR `` that tries to carry more CSS, a player asleep, a thing with
a picture, a DARK thing, a thing whose name has a comma, quotes, parentheses and
a semicolon, exits plain (with `CONFIRM`), locked to the wizard, DARK and
unlinked, and a description with a literal newline and a `%N`. For each viewer
they assert valid JSON with `v: 2`; the DARK wizard, thing and exit absent for
the mortal and present for the wizard; the locked exit `locked` with its hint
and no `dest` for the mortal, `open` with `dest` for the wizard; the hostile
colour dropped and the good one kept; the bad focal dropped with the payload
still valid; `you: true` on the viewer's own row only; and `room.info` with
`v`, `objid`, `name`, `image.url` and the evaluated description. The scene
block is tested against `@function` stand-ins for the plugin's answers
(`#-1 NOT FOUND` for no scene; a real id, `public` and members for one). One
test reads the registers ``FN`PREPARE`` fills; one runs the shipped handler
itself, unmodified, for every cause, and checks `room.info` goes to the mover
alone when the mover is the enactor, and to everyone when the enactor is a
thing in the room. Two more check that `room.info` takes ``IMAGE`BANNER``
(falling back to `IMAGE`) and that an image attribute without `visual` is
not published.

`SharpMUSH.Tests.Integration/Packages/RoomContentsPackageTests.cs` covers the
delivery side: that the bundled `room-contents` package is installed at boot and
that its attributes land on the configured event handler.

---

## JSON payload shapes (v2)

As the tests built them. The room holds a DARK wizard (`#13`), a mortal
(`#14`), a thing with a picture (`#20`), a DARK crate (`#21`) and a thing with
a hostile name (`#22`).

### `room.contents`

For the wizard — everyone, `you` on its own row, its colour kept, the mortal's
hostile colour and bad focal dropped:

```json
{"v":2,"who":[
  {"dbref":"#13","objid":"#13:1790741467794","type":"player","name":"RcWiz_AZovdJr3wSw_1","cmd":"look #13",
   "color":"#ffb454","status":"active","idle":1,"profile":true,"you":true},
  {"dbref":"#14","objid":"#14:1790741467825","type":"player","name":"RcMortal_AZovdJr3wSw_2","cmd":"look #14",
   "image":{"url":"/assets/chars/f12d1d09.jpg","alt":"RcMortal_AZovdJr3wSw_2"},
   "status":"active","idle":1,"profile":true,"actions":[{"label":"Page","cmd":"page #14="}]},
  {"dbref":"#20","objid":"#20:1790741467927","type":"thing","name":"Oilcloth bundle f12d1d09","cmd":"look #20",
   "image":{"url":"/assets/obj/f12d1d09.jpg","alt":"Oilcloth bundle f12d1d09"}},
  {"dbref":"#21","objid":"#21:1790741467928","type":"thing","name":"Crate f12d1d09","cmd":"look #21"},
  {"dbref":"#22","objid":"#22:1790741467931","type":"thing","name":"Badf12d1d09, \"quoted\" (thing); $5 <tag>","cmd":"look #22"}]}
```

For the mortal — the DARK wizard and the DARK crate are not there:

```json
{"v":2,"who":[
  {"dbref":"#14","objid":"#14:1790741467825","type":"player","name":"RcMortal_AZovdJr3wSw_2","cmd":"look #14",
   "image":{"url":"/assets/chars/f12d1d09.jpg","alt":"RcMortal_AZovdJr3wSw_2"},"status":"active","idle":1,"profile":true,"you":true},
  {"dbref":"#20","objid":"#20:1790741467927","type":"thing","name":"Oilcloth bundle f12d1d09","cmd":"look #20",
   "image":{"url":"/assets/obj/f12d1d09.jpg","alt":"Oilcloth bundle f12d1d09"}},
  {"dbref":"#22","objid":"#22:1790741467931","type":"thing","name":"Badf12d1d09, \"quoted\" (thing); $5 <tag>","cmd":"look #22"}]}
```

`you` appears on one row per payload; `actions` never on your own row; the
player-only keys (`status`, `idle`, `profile`, `actions`, `color`) never on a
thing; `image` only where `IMAGE` is set and `visual`; `color` only when it is `#rrggbb`.

### `room.exits`

For the mortal — the DARK exit is not there, and the locked one is `locked`
with its hint and no `dest`:

```json
{"v":2,"exits":[
  {"dbref":"#28","objid":"#28:1790741469223","name":"north7b789db9","aliases":["n7b789db9"],"cmd":"goto #28",
   "confirm":"This leaves the scene.","state":"open","dest":{"name":"RcDest7b789db9","desc":"A lamplit street.","here":0}},
  {"dbref":"#29","objid":"#29:1790741469224","name":"east7b789db9","aliases":["e7b789db9"],"cmd":"goto #29",
   "state":"locked","hint":"Closed after dusk."},
  {"dbref":"#31","objid":"#31:1790741469226","name":"west7b789db9","aliases":[],"cmd":"goto #31","state":"closed"}]}
```

For the wizard — four exits, `east` is `open` with its `dest`, and `secret`
(DARK) is listed:

```json
{"v":2,"exits":[
  {"dbref":"#28","objid":"#28:1790741469223","name":"north7b789db9","aliases":["n7b789db9"],"cmd":"goto #28",
   "confirm":"This leaves the scene.","state":"open","dest":{"name":"RcDest7b789db9","desc":"A lamplit street.","here":0}},
  {"dbref":"#29","objid":"#29:1790741469224","name":"east7b789db9","aliases":["e7b789db9"],"cmd":"goto #29",
   "state":"open","dest":{"name":"RcDest7b789db9","desc":"A lamplit street.","here":0}},
  {"dbref":"#30","objid":"#30:1790741469225","name":"secret7b789db9","aliases":[],"cmd":"goto #30",
   "state":"open","dest":{"name":"RcDest7b789db9","desc":"A lamplit street.","here":0}},
  {"dbref":"#31","objid":"#31:1790741469226","name":"west7b789db9","aliases":[],"cmd":"goto #31","state":"closed"}]}
```

- `state` is `open | locked | closed`. `closed` is an exit that leads nowhere;
  a HOME or VARIABLE destination stays `open` and has no `dest`.
- `cmd` stays on a locked exit: locks are dynamic, and trying one is how a
  player reads its `@fail`.
- `dest.area` and `dest.image` follow the same omit-when-unset rule as the
  room's own; `dest.here` counts connected, non-DARK players.

### `room.info`

With a parent (hence `area`), and — with a scene stub answering for the
plugin — a public scene:

```json
{"v":2,"dbref":"#35","objid":"#35:1790741470197","name":"RcRoom12459103",
 "desc":{"format":"text","text":"Tarred pilings.\nStacked crates, God looks on."},
 "area":"RcDest12459103",
 "image":{"url":"/assets/rooms/12459103.jpg","alt":"The quay at dusk","focal":[0.5,0.6]}}
```

```json
{"v":2,"dbref":"#47","objid":"#47:1790741471143","name":"RcRoom468adfc9",
 "desc":{"format":"text","text":"Tarred pilings.\nStacked crates, God looks on."},
 "image":{"url":"/assets/rooms/468adfc9.jpg","alt":"The quay at dusk","focal":[0.5,0.6]},
 "scene":{"id":"42","title":"Salt Market at Dusk","cast":3}}
```

- `desc.format` is `text`: the description is the evaluated `DESCRIBE`, with
  markup stripped by `oob()`; the `%r` became a newline and the `%N` the
  enactor's name. Nothing produces markdown here.
- `area` appears once the room has a zone or a parent; `image` once it has a
  visual ``IMAGE`BANNER`` or `IMAGE` — the banner is the wide art, and `url`
  is the banner when both are set; `scene` (`{"id","title","cast"}`, `id` a string) once a scene the
  viewer may see runs in the room. Otherwise the key is absent.
- There are no `width`/`height` on an image: every portal surface is a
  fixed-size box the picture is cropped into.

---

## v1 (still accepted by the client)

1.0 sent one payload to the whole room, with no `v`:

```json
{ "who": [ { "dbref": "#76", "name": "Marble Bust", "cmd": "look #76" },
           { "dbref": "#1",  "name": "God",         "cmd": "look #1"  } ] }
```

```json
{ "exits": [ { "name": "east",  "cmd": "goto #80" },
             { "name": "north", "cmd": "goto #74" } ] }
```

Every v1 key is unchanged in v2 — `dbref`, `name`, `cmd` on a who row;
`name`, `cmd` on an exit row — so a consumer that reads only those and
ignores the rest keeps working, and a payload without `v` is read as v1. The
portal (Blazor WASM) routes incoming OOB frames by package name
(`room.contents`, `room.exits`, `room.info`) into its per-connection OOB
channel store; the Play sidebar renders `who`/`exits` entries, blank names as
"(untitled)", and issues each entry's `cmd` on click.
