# WebSocket Support Package — Reference `ROOM`CONTENTS` Handler

This document is part of the **WebSocket Support Package**. It provides a
reference implementation of the `ROOM`CONTENTS` event handler that fans out
structured OOB pushes to a room's connected occupants whenever the room's
population changes (player movement, connect, or disconnect).

The handler below was verified end-to-end against a live server: installed on
`#9`, fired by the real event path, and confirmed to populate the portal's
Play sidebar.

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

For the connected occupants of that room it sends two OOB packages over each
WebSocket (or GMCP) connection:

- **`room.contents`** — `{"who": [ {dbref, name, cmd}, … ]}`, one entry per
  non-exit occupant (things and players).
- **`room.exits`** — `{"exits": [ {name, cmd}, … ]}`, one entry per exit, with
  the `goto` command a client issues to traverse it.

The `lcon`/`lexits`/filter calls are the natural customisation seam: add
dark/visibility guards, change which occupants are listed, or swap the JSON
shape to taste. The engine imposes no policy here.

---

## Idioms used (and the traps they avoid)

These were learned the hard way; the reference handler relies on all of them:

1. **`think oob(...)` — no brackets.** When `oob(...)` is the *entire*
   argument to `think`, it evaluates. Brackets (`think [oob(...)]`) are only
   needed when a function call is embedded in surrounding text. The two forms
   are otherwise equivalent.

2. **`oob(<list>, <package>, <json>)` takes a target *list*.** It locates each
   target and delivers only to those that are players with a live WebSocket
   (or GMCP) connection; everything else is skipped. So you pass `lcon(%0)`
   (the whole room) and let `oob()` do the connected/player filtering — no
   manual `@dolist` fan-out is required.

3. **Build JSON arrays with `json_array()`, not `json(array, iter(...))`.**
   `json(array, …)` takes each element as a *separate* argument, so feeding it
   a single `iter()` list cannot work. `json_array(<list>[, <delim>])`
   assembles a list of already-formed JSON values into an array. Produce the
   per-element JSON with `iter()`:
   `json_array(iter(0 1 2 3, json(number, %i0)))` → `[0,1,2,3]`.

4. **Use a non-space delimiter for `json_array`/`iter` when elements contain
   spaces.** A row like `{"cmd":"look #1"}` contains a space, so the default
   space delimiter would split it apart. The handler uses `|`:
   `iter(<list>, <expr>, %b, |)` then `json_array(<that>, |)`.

5. **Filter with a stored attribute, not `#lambda`.** `filter(#9/FN`NOTEXIT,
   lcon(%0))` is clean; the `#lambda/...` inline form mis-splits on commas
   inside the lambda body (e.g. `hasflag(%0,connected)`) and needs escapes you
   should not have to think about.

6. **`lcon(%0)` includes exits in this engine.** Filter them out of the *who*
   list with a `not(hastype(%0,exit))` predicate so exits don't appear as
   occupants.

7. **Connected detection.** `hasflag(%0,connected)` returns `0` here — use
   `conn(%0)` (seconds, `-1` if not connected) or membership in `lwho()`.
   (The reference handler doesn't need an explicit connected filter because
   `oob()` already skips non-connected targets, per point 2.)

8. **`num(%0)` returns the `#N` dbref form** (e.g. `#76`), so don't prepend an
   extra `#`.

---

## Reference handler

Install on the configured event handler object (default `#9`), one attribute
at a time. The helper attributes keep the main handler readable.

```mushcode
&FN`NOTEXIT #9=not(hastype(%0,exit))
&FN`WHOROW #9=json(object,dbref,json(string,[num(%0)]),name,json(string,name(%0)),cmd,json(string,look [num(%0)]))
&FN`EXITROW #9=json(object,name,json(string,name(%0)),cmd,json(string,goto [num(%0)]))
&ROOM`CONTENTS #9=think oob(lcon(%0),room.contents,json(object,who,json_array(iter(filter(#9/FN`NOTEXIT,lcon(%0)),u(#9/FN`WHOROW,itext(0)),%b,|),|)));think oob(lcon(%0),room.exits,json(object,exits,json_array(iter(lexits(%0),u(#9/FN`EXITROW,itext(0)),%b,|),|)))
```

Reading the main handler:

- `filter(#9/FN`NOTEXIT,lcon(%0))` — room contents minus exits (the *who* set).
- `iter(<set>, u(#9/FN`WHOROW,itext(0)), %b, |)` — build one JSON object per
  occupant (via the `FN`WHOROW` helper, `%0` = the occupant), joined with `|`.
- `json_array(<that>, |)` — assemble those JSON objects into a JSON array.
- `json(object, who, <array>)` — wrap as `{"who": [...]}`.
- `oob(lcon(%0), room.contents, <json>)` — send to every connected occupant of
  the room (non-players / non-connected are skipped automatically).
- The second statement does the same for `room.exits` via `FN`EXITROW`/`lexits`.

Verify it is set:

```
> get #9/ROOM`CONTENTS
```

---

## Removing the handler

```
&ROOM`CONTENTS #9=
```

An empty `&` sets the attribute to an empty string, which silences the handler
(the engine still fires the event, but the attribute executes nothing).

---

## Testing the handler

`SharpMUSH.Tests/Services/RoomContentsHandlerReferenceTests.cs` installs the
reference helpers + handler and triggers `ROOM`CONTENTS` via the **real event
path** — `EventService.TriggerEventAsync(parser, SharpEvents.RoomContents, enactor, room, cause)` — not `@trigger`. The real path runs the handler with
God (`#1`) as the executor — the elevated context the handler needs — whereas
`@trigger` would run it as `#9` and the introspection calls would silently
return nothing.

The test asserts the handler emits a **valid JSON `room.contents` payload**
with the room's occupants — exercising `oob()`, `json_array()`, `iter()`,
`filter()`, and the helper attributes together (the gap the previous,
simplified test left open).

---

## JSON payload shapes

### `room.contents`

```json
{
  "who": [
    { "dbref": "#76", "name": "Marble Bust",    "cmd": "look #76" },
    { "dbref": "#1",  "name": "God",            "cmd": "look #1"  }
  ]
}
```

### `room.exits`

```json
{
  "exits": [
    { "name": "east",  "cmd": "goto #80" },
    { "name": "north", "cmd": "goto #74" }
  ]
}
```

The portal (Blazor WASM) routes incoming OOB frames by package name
(`room.contents`, `room.exits`) into its per-connection OOB channel store; the
Play sidebar renders `who`/`exits` entries, blank names as "(untitled)", and
issues each entry's `cmd` on click.
