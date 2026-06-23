# WebSocket Support Package — Reference `ROOM`CONTENTS` Handler

This document is part of the **WebSocket Support Package**. It provides a
reference implementation of the `ROOM`CONTENTS` event handler that fans out
structured OOB pushes to the room's connected occupants whenever the room's
population changes (player movement, connect, or disconnect).

> **Packaging note:** The eventual home for this softcode is a SharpMUSH
> package loadable via the `@package` command. Until the package-manager
> integration ships as a follow-up, install the handler manually as shown
> below.

---

## What the handler does

When `ROOM`CONTENTS` fires the handler receives:

| Register | Value |
|----------|-------|
| `%0` | Dbref of the affected room |
| `%1` | Cause: `move-in`, `move-out`, `connect`, or `disconnect` |

For every *connected* occupant of that room the handler sends two OOB
packages over the WebSocket (or GMCP) connection:

- **`room.contents`** — a JSON object listing the room's name and the
  name of every current occupant (non-exit things and players).
- **`room.exits`** — a JSON object listing each exit's name and the
  bare `goto` command a client can issue to traverse it.

The exit/contents filtering is the natural customisation seam: change
the `lcon` or `lexits` calls, add dark/visibility guards, or swap the
JSON shape to taste. The engine imposes no policy here.

---

## SharpMUSH function availability notes

Before installing, note these verified behaviours of the functions used:

| Call in handler | Status |
|-----------------|--------|
| `lcon(%0)` | **Exists.** Returns a space-separated list of all contents dbrefs. |
| `lcon(%0,connected)` | **Second arg is silently ignored** — the engine's `lcon` accepts up to 2 args but does not implement a filter flag. Use `filter(#lambda/hasflag(%0,connected),lcon(%0))` to restrict to connected occupants. |
| `lexits(%0)` | **Exists.** Returns a space-separated list of exit dbrefs in the room. |
| `json(object,k,v,...)` / `json(array,...)` / `json(string,...)` | **All exist.** |
| `iter(list, expr)` / `itext(0)` | **Both exist.** |
| `name(dbref)` | **Exists.** |
| `secure(str)` | **Exists.** |
| `words(list)` | **Exists.** |
| `@dolist list={action}` | **Exists.** |
| `oob(target, package, json)` | **Exists** (built in Tasks A1–A2). |
| `hasflag(dbref, connected)` | **Exists** via the `CONNECTED` pseudo-flag. |
| `filter(#lambda/expr, list)` | **Exists** — `#lambda/` inline form supported. |

---

## Reference handler

Install on the configured event handler object (default: `#9`):

```mushcode
&ROOM`CONTENTS #9=
  @dolist [filter(#lambda/hasflag(%0,connected),lcon(%0))]={
    think oob(##, room.contents,
      [json(object,
        who,  [json(array, [iter(lcon(%0), json(string,name(itext(0))))])],
        room, [json(string,name(%0))])]);
    think oob(##, room.exits,
      [json(object,
        exits, [json(array, [iter(lexits(%0),
          json(object,
            name, json(string,name(itext(0))),
            cmd,  json(string,goto itext(0))))])])])
  }
```

### Key design points

- `filter(#lambda/hasflag(%0,connected),lcon(%0))` — restricts fan-out to
  connected players only. `lcon` does not implement a native `connected`
  flag argument in this version of SharpMUSH; the lambda filter is the
  correct idiom.
- `@dolist … ##` — `##` is the current list item (a connected occupant
  dbref). Each occupant receives its own pair of OOB messages.
- `lcon(%0)` inside the `room.contents` payload lists *all* current
  occupants, not just the connected ones — this is intentional so the
  browser can render the full room population including NPCs.
- `lexits(%0)` lists exits; the `goto itext(0)` string is the command a
  browser client sends back to traverse the exit.
- The handler calls `think oob(…)` so the return values of `oob()` do not
  appear in the room (they are sent to no output channel).

---

## Installation

```
@trigger me/INSTALL_WS_PACKAGE
```

Or manually, one attribute at a time:

```
&ROOM`CONTENTS #9=@dolist [filter(#lambda/hasflag(%0,connected),lcon(%0))]={think oob(##, room.contents, [json(object, who, [json(array, [iter(lcon(%0), json(string,name(itext(0))))])], room, [json(string,name(%0))])]); think oob(##, room.exits, [json(object, exits, [json(array, [iter(lexits(%0), json(object, name, json(string,name(itext(0))), cmd, json(string,goto itext(0))))])])])}
```

Verify the handler is set:

```
> get #9/ROOM`CONTENTS
```

---

## Removing the handler

```
&ROOM`CONTENTS #9=
```

An empty `&` sets the attribute to an empty string, which effectively
silences the handler (the engine fires it but it executes nothing).

---

## Testing the handler

The wiring test (`RoomContentsHandlerReferenceTests`) installs a
simplified variant of this handler — one that records the count of
connected occupants it processed — triggers `ROOM`CONTENTS` via
`@trigger #9/ROOM`CONTENTS=<room>,move-in`, then asserts that the
recorded count matches an independently-computed `words(lcon(room))`
value. This proves:

1. The handler attribute is executed when `ROOM`CONTENTS` fires.
2. `%0` correctly carries the room dbref into the handler body.
3. The `lcon(%0)` call returns the room's occupants from within the
   handler context.

See `SharpMUSH.Tests/Services/RoomContentsHandlerReferenceTests.cs`.

---

## JSON payload shapes

### `room.contents`

```json
{
  "who":  ["God", "Alice"],
  "room": "The Void"
}
```

### `room.exits`

```json
{
  "exits": [
    { "name": "North",  "cmd": "goto #42" },
    { "name": "Out",    "cmd": "goto #7"  }
  ]
}
```

The client (Blazor WASM) routes incoming OOB packages by the package
name (`room.contents`, `room.exits`) and updates its reactive room
model accordingly.
