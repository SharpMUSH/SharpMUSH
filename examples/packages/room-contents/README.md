# room-contents

The `` ROOM`CONTENTS `` event handler, delivered as an attach-mode package
(decision 20.3). It is what makes the web portal's **Play page** (the room
banner, the *Here* and *Exits* cards) populate.

The engine fires `` ROOM`CONTENTS `` room-scoped whenever a room's population
changes — movement, connect, disconnect — with `%0` = the affected room and
`%1` = the cause (`move-in`, `move-out`, `connect`, `disconnect`). This package
supplies the attribute that turns that event into **OOB v2** pushes, built once
per connected occupant and sent to that occupant alone:

- **`room.contents`** — `{"v": 2, "who": [ … ]}`, one row per non-exit
  occupant; players appear only while CONNECTED, matching PennMUSH.
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

2.0 builds every payload for one viewer at a time. Three things differ by who
is looking:

- a **DARK** exit is omitted unless the viewer is Wizard/Royalty or holds
  `See_All` (`` FN`EXITVIS ``);
- an exit's `state` is `locked` when its Basic lock fails **for that viewer**,
  and only then does the row carry the exit's `@fail` as `hint`;
- the viewer's own row in `who` carries `"you": true`.

`room.info` goes to every connected occupant of the room on arrival causes.
`%#` in the handler is the object that *caused* the event — the wizard who
`@tel`'d someone, or God for a system move — not reliably the mover, so the
handler does not try to single the mover out; the mover is in `lcon(%0)` by
the time the event fires, and a client already holding the same `room.info`
can ignore a repeat.

## Pictures

Rows and `room.info` carry `image` as `{"url", "alt", "focal"}` read from the
seeded `IMAGE` / `` IMAGE`ALT `` / `` IMAGE`FOCAL `` attributes (`help IMAGE`).
`alt` falls back to the object's name, `focal` is present only when set, and
an object with no `IMAGE` carries **no `image` key** rather than a null one.
Set them with `&IMAGE here=/assets/rooms/docks.jpg`.

## Customising

Every seam is one attribute on the handler object; the package's three-way
merge keeps a local edit across upgrades.

| Attribute | Decides |
|---|---|
| `` FN`WHOVIS `` (`%0` occupant) | who is listed in `who` |
| `` FN`VIEWER `` (`%0` occupant) | who receives pushes |
| `` FN`EXITVIS `` (`%0` exit, `%1` viewer) | which exits a viewer sees |
| `` FN`STATUS `` (`%0` idle seconds) | `active` / `idle` / `away` thresholds |
| `` FN`AREA `` (`%0` room) | the `area` name: zone, else parent, else omitted |
| `` FN`DESC `` (`%0` room) | the description text pushed (evaluated as the room; blank it to push none) |
| `` FN`EXITHINT `` (`%0` exit) | the hint on a locked exit (default: its `@fail`, unevaluated) |
| `` FN`SCENE `` (`%0` room, `%1` viewer) | the `scene` block, or null |
| `` FN`WHOROW `` / `` FN`EXITROW `` (`%0` row subject, `%1` viewer) | one row each |
| `` FN`PAYLOAD`CONTENTS `` / `` FN`PAYLOAD`EXITS `` / `` FN`PAYLOAD`INFO `` (`%0` room, `%1` viewer) | one payload each |

Two per-exit attributes are read as data: `CONFIRM` (a confirmation the client
shows before traversing, e.g. `&CONFIRM out=This leaves the scene.`) and the
exit's `@fail`, used as the locked hint. A per-player `` PROFILE`COLOR ``
becomes the row's `color`.

Rows are a base object every consumer gets, `json_mod()`-patched with the
optional keys; a `null` in a merge patch **removes** the key, which is how a
row without a picture, or a row that is not you, carries nothing rather than
`null`.

## v1 (still accepted by the client)

1.0 sent one `room.contents` / `room.exits` to the whole room, with rows of
`{dbref, name, cmd}` and `{name, cmd}` and no `v`. Every v1 key is unchanged
in 2.0 — v2 only adds — so a consumer that reads `dbref`, `name` and `cmd` and
ignores the rest keeps working, and a payload without `v` is read as v1.
