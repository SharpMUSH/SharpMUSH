# WebSocket Support Package — the ``ROOM`CONTENTS`` Handler

This document is part of the **WebSocket Support Package**. It describes the
``ROOM`CONTENTS`` event handler that fans out structured OOB pushes to a room's
connected occupants whenever the room's population changes (player movement,
connect, or disconnect) — the data source behind the portal's Play page.

> **It ships installed.** The handler is the bundled **`room-contents`**
> package (`examples/packages/room-contents/`), installed at first boot by
> `DefaultPackagesBootstrapService` onto the configured `event_handler` object
> (`{{$event_handler}}`, `#9` by default). Nothing below needs typing on a
> stock game — it is here to explain what is installed and how to change it.
> Manage it like any other package: `@package list`, `@package uninstall
> room-contents`, or edit the attributes directly (the package's three-way
> merge keeps local edits on the next upgrade).

Version 2.0 of the package speaks **OOB v2** (`docs/design/d1/README.md` §7.1,
`docs/superpowers/specs/2026-09-29-image-attributes-and-oob-v2-design.md` §4).
The v1 shapes are at the end; v2 only adds keys to them.

---

## What the handler does

When ``ROOM`CONTENTS`` fires the handler receives:

| Register | Value |
|----------|-------|
| `%0` | Dbref of the affected room |
| `%1` | Cause: `move-in`, `move-out`, `connect`, or `disconnect` |
| `%#` | The object that **caused** the event — see below |

For **each** connected player in that room it builds that player's own view
and sends it over their WebSocket (or GMCP) connection:

- **`room.contents`** — `{"v": 2, "who": [ … ]}`, one row per non-exit
  occupant (things and players).
- **`room.exits`** — `{"v": 2, "exits": [ … ]}`, one row per exit the viewer
  may see, with the `goto` command a client issues to traverse it.
- **`room.info`** — `{"v": 2, …}`, the room itself: identity, area, picture,
  description, scene. Sent on `move-in` and `connect` only.

Three things in a payload depend on who is looking, which is why one JSON for
the whole room (what 1.0 did) is not enough: a DARK exit is omitted for a
viewer who may not see it, an exit's `state` is `locked` when its lock fails
*for that viewer* (and only then does it carry a `hint`), and the viewer's own
row says `"you": true`.

**`%#` is not the mover.** The event's enactor is whoever caused the change:
the player who walked, but also the wizard who `@tel`'d someone else, or God
(`#1`) for a system move such as a void rescue. The handler therefore does not
try to address `room.info` to "the mover": it sends it to every connected
occupant of the room on the arrival causes, and the mover is in `lcon(%0)` by
the time the event fires. A client already holding the same `room.info` can
ignore a repeat. (A ``ROOM`INFO`` engine event that would fire on a name,
image or description edit is deferred; the engine has no attribute-change
event, and the Play page re-requests through `query.*` instead.)

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

3. **Build JSON arrays with `json_array()`, not `json(array, iter(...))`.**
   `json(array, …)` takes each element as a *separate* argument, so feeding it
   a single `iter()` list cannot work. `json_array(<list>[, <delim>])`
   assembles a list of already-formed JSON values into an array, and answers
   `[]` for an empty list. Produce the per-element JSON with `iter()`.

4. **Use `%r` as the `json_array`/`iter` separator.** A row embeds names,
   descriptions and hints, which can contain a space or a `|`; it cannot
   contain a raw newline once `json(string, …)` has escaped it. 1.0 used `|`,
   which a description can contain.

5. **Optional keys are a `json_mod(<base>, patch, <patch>)`.** Function
   arguments are split before evaluation, so a key/value pair cannot be
   conditionally *inserted* into `json(object, …)`. Instead every row is a
   base object plus a merge patch (RFC 7396): a `null` value in the patch
   **removes** the key. ``FN`IMAGE`` answers the word `null` for an object
   with no `IMAGE`, and the row comes out without an `image` key at all.

6. **Filter with a stored attribute, not `#lambda`.** ``filter(me/FN`WHOVIS,
   lcon(%0))`` is clean; the `#lambda/...` inline form mis-splits on commas
   inside the lambda body. `filter()` passes its arguments from the fifth on
   to the predicate as `%1…`, which is how ``FN`EXITVIS`` gets the viewer.

7. **`lcon(%0)` includes exits in this engine.** Filter them out of the *who*
   list with `not(hastype(%0,exit))` so exits don't appear as occupants.

8. **Connected detection.** `hasflag(%0,CONNECTED)` reflects presence: true
   for a player with a live *play* session, false for a disconnected or
   portal-only (background) connection. ``FN`WHOVIS`` uses it to keep asleep
   players out of the *who* list. There is no `isplayer()` on this engine
   (nor on PennMUSH) — 1.0 called one, and `not()` of the resulting error was
   true, so every disconnected player was listed. Use `hastype(%0,player)`.

9. **An unknown function is literal text**, as on PennMUSH: without the Scene
   plugin, `scenewhere(#12)` evaluates to `scenewhere(#12)`, not to an error.
   ``FN`SCENE`` therefore asks `scene(<id>, id)` to echo the id it was given,
   which only a real, visible scene does.

10. **`objeval(<obj>, <expr>)` evaluates the object argument** (fixed
    alongside 2.0 to match `fun_objeval`): ``FN`DESC`` runs a room's
    `DESCRIBE` as the room, the executor `look` gives it, so its softcode
    runs with the room's permissions and not the wizard handler's.

11. **`num(%0)` returns the `#N` dbref form** (e.g. `#76`), so don't prepend
    an extra `#`. Side-effect builders (`dig()`, `open()`, `create()`) answer
    an **objid** (`#76:1790738802944`); `num()` it before comparing.

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
| ``FN`WHOVIS`` | the occupant is listed: not an exit, and if a player, CONNECTED |
| ``FN`VIEWER`` | the occupant receives pushes: a CONNECTED player |
| ``FN`ONLINE`` | a player counts toward a destination's `here` |
| ``FN`EXITVIS`` | the viewer may see the exit: not DARK, or LIGHT, or the viewer is Wizard/Royalty/`See_All` |

**Value helpers.**

| Attribute | Gives |
|---|---|
| ``FN`STATUS`` (`%0` idle seconds) | `active` under 5 min, `idle` under 30, else `away` |
| ``FN`AREA`` (`%0` room) | the name of the room's zone, else its parent, else nothing |
| ``FN`IMAGE`` (`%0` object) | `{"url","alt","focal"}` from `IMAGE`/``IMAGE`ALT``/``IMAGE`FOCAL``, or `null` |
| ``FN`DESC`` (`%0` room) | the room's `DESCRIBE`, evaluated as the room |
| ``FN`SCENE`` (`%0` room, `%1` viewer) | `{"id","title","cast"}` for a scene the viewer may see, or `null` |
| ``FN`EXITHINT`` (`%0` exit) | the exit's `@fail`, unevaluated |
| ``FN`EXITSTATE`` (`%0` exit, `%1` viewer, `%2` loc(exit)) | `closed` when unlinked, `locked` when the Basic lock fails for the viewer, else `open` |
| ``FN`DEST`` (`%0` destination, `%1` viewer) | `{"name","area","image","desc","here"}` |

**Rows and payloads.**

```mushcode
&FN`WHOROW #9=json_mod(json(object,dbref,json(string,num(%0)),objid,json(string,objid(%0)),type,json(string,lcstr(type(%0))),name,json(string,name(%0)),cmd,json(string,look [num(%0)])),patch,json(object,image,u(me/FN`IMAGE,%0),you,if(strmatch(num(%0),num(%1)),true,null),color,if(setr(c,get(%0/PROFILE`COLOR)),json(string,%q<c>),null),status,if(hastype(%0,player),json(string,u(me/FN`STATUS,idle(%0))),null),idle,if(hastype(%0,player),json(number,idle(%0)),null),profile,if(hastype(%0,player),true,null),actions,if(cand(hastype(%0,player),not(strmatch(num(%0),num(%1)))),json(array,json(object,label,json(string,Page),cmd,json(string,page [num(%0)]=))),null)))
&FN`EXITROW #9=json_mod(json(object,dbref,json(string,num(%0)),objid,json(string,objid(%0)),name,json(string,name(%0)),aliases,json_array(iter(fullalias(%0),json(string,%i0))),cmd,json(string,goto [num(%0)]),state,json(string,setr(st,u(me/FN`EXITSTATE,%0,%1,setr(d,loc(%0)))))),patch,json(object,hint,if(cand(strmatch(%q<st>,locked),setr(h,u(me/FN`EXITHINT,%0))),json(string,%q<h>),null),confirm,if(setr(c,get(%0/CONFIRM)),json(string,%q<c>),null),dest,if(isdbref(%q<d>),u(me/FN`DEST,%q<d>,%1),null)))
&FN`PAYLOAD`CONTENTS #9=json(object,v,json(number,2),who,json_array(iter(filter(me/FN`WHOVIS,lcon(%0)),u(me/FN`WHOROW,%i0,%1),,%r),%r))
&FN`PAYLOAD`EXITS #9=json(object,v,json(number,2),exits,json_array(iter(if(hastype(%0,room),filter(me/FN`EXITVIS,lexits(%0),,,%1)),u(me/FN`EXITROW,%i0,%1),,%r),%r))
&FN`PAYLOAD`INFO #9=json_mod(json(object,v,json(number,2),dbref,json(string,num(%0)),objid,json(string,objid(%0)),name,json(string,name(%0)),desc,json(object,format,json(string,text),text,json(string,u(me/FN`DESC,%0)))),patch,json(object,area,if(setr(a,u(me/FN`AREA,%0)),json(string,%q<a>),null),image,u(me/FN`IMAGE,%0),scene,u(me/FN`SCENE,%0,%1)))
&ROOM`CONTENTS #9=think null(iter(filter(me/FN`VIEWER,lcon(%0)),[oob(%i0,room.contents,u(me/FN`PAYLOAD`CONTENTS,%0,%i0))][oob(%i0,room.exits,u(me/FN`PAYLOAD`EXITS,%0,%i0))][if(match(move-in connect,%1),oob(%i0,room.info,u(me/FN`PAYLOAD`INFO,%0,%i0)))]))
```

Reading the main handler:

- ``filter(me/FN`VIEWER, lcon(%0))`` — the viewers: connected players in the room.
- `iter(<viewers>, …)` — for each, with `%i0` the viewer:
  - ``oob(%i0, room.contents, u(me/FN`PAYLOAD`CONTENTS, %0, %i0))`` — build
    the who list as this viewer sees it and send it to this viewer alone;
  - the same for `room.exits`;
  - `if(match(move-in connect, %1), …)` — on the arrival causes, `room.info` too.
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

- `IMAGE`, ``IMAGE`ALT``, ``IMAGE`FOCAL`` on any object (`help IMAGE`) — the picture.
- ``PROFILE`COLOR`` on a player — the row's `color`.
- `CONFIRM` on an exit — a confirmation the client shows before traversing.
- `@fail` on an exit — the `hint` on a locked row.
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
and substitute only the handler, with one that stores what a payload builder
answers for a named viewer — ``&LAST_PAYLOAD #9=[u(me/FN`PAYLOAD`EXITS,%0,#13)]``
— instead of sending it, since `oob()` needs a live WebSocket the harness
lacks. Against a purpose-built room (a wizard and a mortal connected, a player
asleep, a thing with a picture and one without, and exits plain, locked, DARK
and unlinked) they assert, for each viewer, valid JSON with `v: 2`; the locked
exit `locked` with its hint for the mortal and `open` for the wizard; the DARK
exit absent for the mortal; `you: true` on the viewer's own row only; and
`room.info` with `v`, `objid`, `name` and `image.url` from `&IMAGE`. A last
test runs the shipped handler itself, unmodified, for every cause.

`SharpMUSH.Tests.Integration/Packages/RoomContentsPackageTests.cs` covers the
delivery side: that the bundled `room-contents` package is installed at boot and
that its attributes land on the configured event handler.

---

## JSON payload shapes (v2)

As the tests built them, for a mortal viewer (`#13`) in a room with a wizard
(`#12`), a thing with a picture and one without:

### `room.contents`

```json
{"v":2,"who":[
  {"dbref":"#12","objid":"#12:1790738802944","type":"player","name":"RcWiz_52cA8g_eypU_1","cmd":"look #12",
   "status":"active","idle":0,"profile":true,"actions":[{"label":"Page","cmd":"page #12="}]},
  {"dbref":"#13","objid":"#13:1790738802967","type":"player","name":"RcMortal_52cA8g_eypU_2","cmd":"look #13",
   "image":{"url":"/assets/chars/7b1abf4e.jpg","alt":"RcMortal_52cA8g_eypU_2"},"you":true,"color":"#ffb454",
   "status":"active","idle":0,"profile":true},
  {"dbref":"#19","objid":"#19:1790738803092","type":"thing","name":"Oilcloth bundle 7b1abf4e","cmd":"look #19",
   "image":{"url":"/assets/obj/7b1abf4e.jpg","alt":"Oilcloth bundle 7b1abf4e"}},
  {"dbref":"#20","objid":"#20:1790738803095","type":"thing","name":"Crate 7b1abf4e","cmd":"look #20"}]}
```

`you` appears on one row per payload; `actions` never on your own row; the
player-only keys (`status`, `idle`, `profile`, `actions`, `color`) never on a
thing; `image` only where `IMAGE` is set.

### `room.exits`

For the mortal — the DARK exit is not there, and the locked one is `locked`
with its hint:

```json
{"v":2,"exits":[
  {"dbref":"#26","objid":"#26:1790738804041","name":"north31c071df","aliases":["n31c071df"],"cmd":"goto #26","state":"open",
   "confirm":"This leaves the scene.","dest":{"name":"RcDest31c071df","desc":"A lamplit street.","here":0}},
  {"dbref":"#27","objid":"#27:1790738804042","name":"east31c071df","aliases":["e31c071df"],"cmd":"goto #27","state":"locked",
   "hint":"Closed after dusk.","dest":{"name":"RcDest31c071df","desc":"A lamplit street.","here":0}},
  {"dbref":"#29","objid":"#29:1790738804044","name":"west31c071df","aliases":[],"cmd":"goto #29","state":"closed"}]}
```

For the wizard, the same room has four exits, `east` is `open` with no
`hint`, and `secret` (DARK) is listed.

- `state` is `open | locked | closed`. `closed` is an exit that leads nowhere;
  a HOME or VARIABLE destination stays `open` and has no `dest`.
- `cmd` stays on a locked exit: locks are dynamic, and trying one is how a
  player reads its `@fail`.
- `dest.area` and `dest.image` follow the same omit-when-unset rule as the
  room's own.

### `room.info`

```json
{"v":2,"dbref":"#32","objid":"#32:1790738804713","name":"RcRoome7d4f144",
 "desc":{"format":"text","text":"Tarred pilings and stacked crates."},
 "area":"RcDeste7d4f144",
 "image":{"url":"/assets/rooms/e7d4f144.jpg","alt":"The quay at dusk","focal":[0.5,0.6]}}
```

- `desc.format` is `text`: the description is the evaluated `DESCRIBE`, with
  markup stripped by `oob()`. Nothing produces markdown here.
- `area` appears once the room has a zone or a parent; `image` once it has an
  `IMAGE`; `scene` (`{"id","title","cast"}`, `id` a string) once a scene the
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
