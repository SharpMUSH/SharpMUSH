# Command palette

Ctrl+K opens `CommandPalette` (`SharpMUSH.Client/Components/CommandPalette.razor`). It goes to portal
pages and runs game commands. Packages, builders and players add commands to it; what the player has
been doing decides what comes first. Help topics `palette entries`, `@context` and `context()` are
the player and builder reference; this document is the model behind them.

## Model

- **Entries** are attributes ``PALETTE`<NAME>`` on the object whose command they run. The value is a
  JSON object (label, line with `<slot>`s, `attr`, `action` run or insert, `lock`, `always`, `when`,
  `urgent`, `context`, `info`, `group`, `icon`, `keywords`, `help`, `order`). It is data; only `lock`
  is evaluated.
- **The context stack** is per character, in memory (`IContextStack`, `ContextStack`). A context is a
  name, text values, the time it was last pushed, an optional lifetime and an info line. Its weight is
  `0.5^(age / 15 min)`; it falls off below 0.05, past its lifetime, or off the bottom of a stack of 20.
  A restart empties every stack.
- **Pushes** come from `@context/push` (code acting for the player, `%#`, or for a player its object
  controls) and from the engine: a typed line that runs a `$`-command pushes each matched object's
  system (`ContextUse`, called by `CommandDispatcher` when the line is typed input, not queued code),
  and a line an `@input` session takes pushes its route target's system (`InputSessionService`). An
  object's system is the package that installed it (`sys_package_objects`), else its objid
  (`ISystemNames`).
- **Names are global.** No package owns a name; an add-on raises its entries on a name another package
  pushes without depending on it. The names in use are a convention listed in the Guide's softcode
  skill.
- **Pins** are `PALETTE_PINS` on the character: ids in order (`<objid>/<NAME>`, `cmd:<command>`,
  `go:<path>`). The portal writes them with `PUT api/palette/pins`.

## Who is offered an entry

`PaletteService` walks the command scopes in dispatch order (`CommandScopes.CandidatesIn`, shared with
`CommandDispatcher`). An entry is offered when its object is perceived; answers commands (not
NO_COMMAND or HALT) with `@lock/command` and `@lock/use` passing; is not another player; has an `attr`
that is one of its `$`-commands, or, without one, a built-in first word the player may run
(`ICommandPermission`, shared with the lock functions); and its own lock passes. The player's own
entries skip the answering checks unless they name an `attr`. Running a line never relies on this: it
runs as typed and the command checks again.

While an `@input` session captures the player's lines, entries with `"context":"input"` are read from
the session's route targets, outside the scopes.

`GetPaletteEntriesQuery` reads and parses an object's entries once, cached under the object's
attribute tag, so any attribute write drops it. Limits: 32 per object, 400 per open, farthest scope
dropped first. What is wrong with an object's entries is reported only to someone who may examine it.

## Order

`PaletteSuggestions.Suggest` (in `SharpMUSH.Contracts`, used by the server and the portal) decides
what the stack raises: an entry with `when` needs every named context and takes the lowest weight
among them; without `when` it rises with its own system. Urgent entries first, then weight, then
scope, order and label. Slots are filled from the raising contexts' values (`PaletteLines.Fill`).
`context()` returns these suggestions to softcode, ready ones only unless given an attribute.

The portal (`PaletteRanker`, plain C# with unit tests) shows, with nothing typed: the `@input`
session's rows, Now (urgent), Pinned, Here (the page's rows, entries for this page kind, raised
entries), Always, Go to; eight rows a group with a "more" row. A row shows once, in its first group.
Typed text scores label, line and keywords, plus pins, input, page, nearness, weight, urgency and
staples; built-ins come last and a declared entry with the same line replaces the bare built-in.

## Pages

A page names itself with `<PalettePage Kind="..." Subject="..." Rows="..."/>` (`PaletteContext`):
`play`, `scene`, `wiki`, `editor`, `app:<slug>`. The wiki adds Edit and History, the Softcode Editor
Run and Save.

## Running a line

With the Play terminal connected, the line goes through it, as a command link does, so an `@input`
session sees it. Otherwise it goes through `POST api/commands` and the palette shows the output.
Insert rows go into the Play composer (or wait for the Play page to open). Input rows need the
terminal.

## API

`GET api/palette[?input=<session id>]` as the account session's bound character answers entries,
the input session, the built-ins the character may run, pins, the stack (top first), and the dropped
count and invalid entries. Opening the palette never waits on it.
