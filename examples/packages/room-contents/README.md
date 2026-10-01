# room-contents

The `` ROOM`CONTENTS `` event handler, delivered as an attach-mode package
(decision 20.3). It is what makes the web portal's **Play page** (the room
banner, the *Here* and *Exits* cards) populate.

The engine fires `` ROOM`CONTENTS `` room-scoped whenever a room's population
changes — movement, connect, disconnect — with `%0` = the affected room and
`%1` = the cause (`move-in`, `move-out`, `connect`, `disconnect`). This package
supplies the attribute that turns that event into **OOB v2** pushes, built for
one connected occupant at a time and sent to that occupant alone:

- **`room.contents`** — `{"v": 2, "who": [ … ]}`, one row per non-exit
  occupant the viewer may see; players appear only while CONNECTED, matching
  PennMUSH.
- **`room.exits`** — `{"v": 2, "exits": [ … ]}`, one row per exit the viewer
  may see, with the `goto` command a client issues to traverse it.
- **`room.info`** — `{"v": 2, …}`, the room itself: identity, area, picture,
  description and the scene running in it. Sent on `move-in` and `connect`.

The portal routes incoming OOB frames by package name into its per-connection
channel store. Without a handler attribute the engine emits nothing and the
page stays empty.

It manages only these attributes on the configured `event_handler` object
(`{{$event_handler}}`, `#9` by default) — the target is resolved from config at
install time, never a literal dbref. It never creates or destroys the object,
and uninstalling leaves the handler object's other softcode untouched.

The full payload shapes, the idioms the handler relies on, and the test
harness are in
[`docs/softcode/room-contents-handler.md`](../../../docs/softcode/room-contents-handler.md).

## Per viewer

2.0 builds every payload for one viewer at a time. What differs by who is
looking:

- a **DARK** occupant or exit is omitted unless the viewer is Wizard/Royalty,
  holds `See_All`, or is the occupant itself (`` FN`WHOVIS ``,
  `` FN`EXITVIS ``) — the handler runs as a wizard object and `lcon()` shows
  it everything, so the filtering is the package's job;
- an exit's `state` is `locked` when its Basic lock fails **for that viewer**;
  only then does the row carry the exit's `@fail` as `hint`, and only when it
  is *not* locked does it carry the destination preview (`dest`) — a locked
  door does not describe what is behind it;
- the viewer's own row in `who` carries `"you": true`;
- the `scene` block appears only for a viewer who may see the scene.

`room.info` goes to the enactor alone when the enactor is a viewer in the room
(the player who walked in or connected — the usual case), and to every
connected occupant otherwise: `%#` is the object that *caused* the event, which
for `@tel` is the wizard elsewhere, for a system move is God, and may be a thing
that moved itself in, so the mover is then unknown or receives nothing. A client already holding the same `room.info`
can ignore a repeat.

## Cost

One event does the viewer-independent work **once** — `` FN`PREPARE `` builds
every occupant's base row, every exit's base row and destination preview, and
the room's own info into q-registers — and then, per connected viewer, only
what differs by viewer. With K occupants, M exits and N viewers that is
O(K+M) evaluations of descriptions and images plus O(N·(K+M)) cheap patches
(one `elock()` per exit per viewer is the largest of them).

`` FN`DESC `` is the expensive helper: it evaluates a room's `DESCRIBE`, once
for the room and once per exit destination, every event. On a big hub, blank
it — `` &FN`DESC #9= `` — and the page falls back to its own text; blank
`` FN`DEST `` too to drop the destination previews entirely.

## Descriptions and the enactor

`` FN`DESC `` evaluates the description as the room (the executor `look`
gives it) with the **event's enactor** as `%#`/`%N`, once per event. That is
the mover for `goto` and connect, but the wizard for `@tel`, and the result is
pushed to every viewer that gets it — so a description that branches on who
is looking (`hasflag(%#,WIZARD)`, `%N`) shows one branch to everyone. The
handler limits `room.info` to the enactor when the enactor is in the room,
which covers the common case; destination previews go to everyone. A game
whose descriptions branch on the viewer blanks `` FN`DESC ``.

## Pictures

Rows and `room.info` carry `image` as `{"url", "alt", "focal"}` read from the
seeded `IMAGE` / `` IMAGE`BANNER `` / `` IMAGE`ALT `` / `` IMAGE`FOCAL ``
attributes (`help IMAGE`). Rows and destination previews take `IMAGE`, the
thumbnail; `room.info` is the Play page's banner and takes `` IMAGE`BANNER ``,
falling back to `IMAGE`. `alt` falls back to the object's name, `focal` is
present only when set to two numbers each 0–1 (anything else is dropped, never
an error), and an object with no picture carries **no `image` key** rather than
a null one. Set them with `&IMAGE here=/assets/rooms/docks.jpg`.

Only a `visual` image attribute is published. The handler is a wizard and
could read a private one; the seeded `visual` flag is a default for a new
attribute, so an `IMAGE` set before the seed existed, or with `visual` cleared
(`@set here/IMAGE=!visual`), stays off the page.

## Text the rows carry

Everything a player typed reaches the page as **plain text**, read with
`get()` and never evaluated: an exit's `@fail` (the `hint`), its `CONFIRM`,
names. `` PROFILE`COLOR `` is accepted only as `#rrggbb`, because the page puts
it in a CSS custom property; anything else is dropped.

## Customising

Every seam is one attribute on the handler object; the package's three-way
merge keeps a local edit across upgrades.

| Attribute | Decides |
|---|---|
| `` FN`WHOVIS `` (`%0` occupant, `%1` viewer) | who is listed in `who` for a viewer |
| `` FN`VIEWER `` (`%0` occupant) | who receives pushes |
| `` FN`EXITVIS `` (`%0` exit, `%1` viewer) | which exits a viewer sees |
| `` FN`ONLINE `` (`%0` player) | who counts toward a destination's `here` (default: connected and not DARK) |
| `` FN`STATUS `` (`%0` idle seconds) | `active` / `idle` / `away` thresholds |
| `` FN`AREA `` (`%0` room) | the `area` name: zone, else parent, else omitted |
| `` FN`COLOR `` (`%0` player) | the row's `color`, validated |
| `` FN`DESC `` (`%0` room) | the description text pushed (blank it to push none) |
| `` FN`EXITHINT `` (`%0` exit) | the hint on a locked exit (default: its `@fail`, plain text) |
| `` FN`SCENE `` (`%0` room, `%1` viewer) | the `scene` block, or null |
| `` FN`WHOBASE `` / `` FN`EXITBASE `` / `` FN`EXITDEST `` / `` FN`INFOBASE `` | the viewer-independent parts, built once per event by `` FN`PREPARE `` |
| `` FN`WHOROW `` / `` FN`EXITROW `` (`%0` row subject, `%1` viewer) | one row each: the base plus what differs by viewer |
| `` FN`PAYLOAD`CONTENTS `` / `` FN`PAYLOAD`EXITS `` / `` FN`PAYLOAD`INFO `` (`%0` room, `%1` viewer) | one payload each |

Two per-exit attributes are read as data: `CONFIRM` (a confirmation the client
shows before traversing, e.g. `&CONFIRM out=This leaves the scene.`) and the
exit's `@fail`, used as the locked hint. A per-player `` PROFILE`COLOR ``
becomes the row's `color`.

Rows are a base object every viewer gets, `json_mod()`-patched with the
optional keys; a `null` in a merge patch **removes** the key, which is how a
row without a picture, or a row that is not you, carries nothing rather than
`null`.

## v1 (still accepted by the client)

1.0 sent one `room.contents` / `room.exits` to the whole room, with rows of
`{dbref, name, cmd}` and `{name, cmd}` and no `v`. Every v1 key is unchanged
in 2.0 — v2 only adds — so a consumer that reads `dbref`, `name` and `cmd` and
ignores the rest keeps working, and a payload without `v` is read as v1.
