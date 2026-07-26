# room-contents

The `` ROOM`CONTENTS `` event handler, delivered as an attach-mode package
(decision 20.3). It is what makes the web portal's **Play sidebar** (the *Here*
and *Exits* cards) populate.

The engine fires `` ROOM`CONTENTS `` room-scoped whenever a room's population
changes — movement, connect, disconnect — with `%0` = the affected room and
`%1` = the cause (`move-in`, `move-out`, `connect`, `disconnect`). This package
supplies the attribute that turns that event into two OOB pushes to the room's
connected occupants:

- **`room.contents`** — `{"who": [ {dbref, name, cmd}, … ]}`, one entry per
  non-exit occupant; players appear only while CONNECTED, matching PennMUSH.
- **`room.exits`** — `{"exits": [ {name, cmd}, … ]}`, one entry per exit, with
  the `goto` command a client issues to traverse it.

The portal routes incoming OOB frames by package name into its per-connection
channel store; the Play sidebar renders the `who` / `exits` entries and issues
an entry's `cmd` on click. Without a handler attribute the engine emits nothing
and the sidebar stays empty.

It manages only these attributes on the configured `event_handler` object
(`{{$event_handler}}`, `#9` by default) — the target is resolved from config at
install time, never a literal dbref. It never creates or destroys the object,
and uninstalling leaves the handler object's other softcode untouched.

## Customising

The helpers are the intended seam: `` FN`WHOVIS `` decides who is listed (add
dark/visibility guards here), and `` FN`WHOROW `` / `` FN`EXITROW `` build one row
each (`%0` = the occupant/exit). The engine imposes no policy.

`oob(<targets>, <package>, <json>)` takes a target *list* and delivers only to
targets that are players with a live WebSocket (or GMCP) connection, so the
handler passes `lcon(%0)` and lets `oob()` do the filtering.
