# Scene System — the bundled `scene` package

The C# Scene System ships only **mechanism**: storage (`ISceneService`), the wizard
`@scene` command, the `scene…()` softcode functions, the `SCENE_ROOM` flag, the
`game.scene.{id}` realtime broadcast, and the portal. It ships **no policy** — nothing
captures a pose, decides who may start a scene, formats a room emit, or spins up a temp
room. That is all **game policy**, delivered as softcode by the bundled **`scene` package**.

> ## The package is the source of truth
>
> Every knob, capture hook, and `+scene/*` verb the package installs lives in
> **[`examples/packages/scene/package.yaml`](../../examples/packages/scene/package.yaml)**.
> Read that file for the exact, current softcode. **This document describes how the pieces
> fit together; it deliberately does not reproduce the verb bodies** (which would drift out
> of sync). When something here disagrees with the package, the package wins.

## How it's installed

`package.yaml` is embedded into `SharpMUSH.Server`, listed in `BundledPackages.All`, and
installed at first boot by `DefaultPackagesBootstrapService` — like any other default
package. It creates and owns a single **WIZARD** thing named **"Scene Logger"** that carries
all the softcode. Admins customize via the **package manager** (edit the managed attributes,
or fork the package) rather than hand-building an object.

The Logger **must be WIZARD**: it is the `@hook/override` target, runs the wizard-only
`@scene`/`@hook` commands, and its lifecycle attributes (`AINSTALL` once, `STARTUP` every
boot) run as the object itself (`%!`/`me` = the Logger), so self-execution of the
wizard-locked `@hook` works.

## Capture — `@hook/override`

Players pose with the native `pose`/`say`/`semipose` commands and `@emit`. `STARTUP`
establishes an **`@hook/override`** on each of `POSE`, `SAY`, `SEMIPOSE`, and `@EMIT`,
pointing at a capture `$`-command on the Logger. Each hook:

1. **Re-broadcasts** the speech to the room with **`@message/spoof`**. `/spoof` makes the
   *speaker* (`%#`) the notification sender — not the WIZARD Logger — preserving attribution,
   and `@message` evaluates the speaker's per-type `FORMAT\`*` attribute **once per recipient**,
   so speaker-specific ("You say…") and observer-specific ("Name says…") text both render.
2. **Asserts** the room has an **active** scene (`scenewhere(loc(%#))`) **and** the poser is
   focused on it (`scenefocus(%#)`), then **records** the rendered line with `@scene/addpose`
   (explicit author `%#`).

Because the assert runs *after* the re-broadcast, a pose outside any scene behaves exactly
like the un-hooked built-in — it simply isn't recorded. Requiring **active + focused** means
passers-by, and people focused on a different scene in the room, are not logged.

> Only **override** hooks work for capture: `@hook/after`/`/before` run with empty args and
> never see the pose text.

Every capture pattern is a **regexp** `$`-command carrying **both** inline flags, `(?is)`. The
`s` is load-bearing: `$`-commands are matched against the command line *after* evaluation, so a
`%r` in the text is a real line break by then, and without `s` a `.` cannot cross it. The
pattern would not match, the built-in would run instead, and the pose would reach the room and
be **silently** absent from the archive — the failure has no error to look for.

The portal's live composer does **not** take this path. It sends the explicit
`+scene/<mode> <id>=<text>` verbs (below), which name the scene rather than inferring it from
the room — its compose box sits on one scene's page, and the character may not be standing in
that scene's room at all. Those verbs record from anywhere, join the poser to the cast, focus
them on the scene, and emit to the room only when they are actually in it. There is still
**one** stored pose, rendered both to the room and over `game.scene.{id}` (no double-capture,
no echo loop) — the capture hooks deliberately do not fire for them.

Whitespace crosses that boundary as substitutions, not as itself: the compose box encodes every
run of spaces, every indent, every line break and every `;` as `%b` / `%r` / `%;` before the
command leaves the browser, because MUSH evaluation compresses space runs, eats the ones at an
argument's edges, ends the line at a newline and starts a new command at a semicolon.

## Settings (`+config scene`)

There is no `SceneOptions` config category. Every setting is policy, kept on the Scene Logger as
``DATA`CONFIG`<NAME>`` and changed with `+config scene/<setting>=<value>` (`config.admin`):

- `default-status`: what `+scene/create` makes. `active` (the default) logs at once; `new` or
  `scheduled` stages a scene that only logs after `+scene/start`.
- `open-details`: whether an owner may set scene details the game does not list. `yes` by default.
- `recall-rounds`: how many rounds of poses a bare `+scene/recall` shows. `2` by default.
- `schedule-days`: how many days ahead a bare `+scene/upcoming` looks. `30` by default.

The refusal strings (``DATA`DENY`APPROVAL``, ``DATA`DENY`NOFOCUS``, ``DATA`DENY`NOTOWNER`` and the
rest) stay plain ``DATA`` attributes on the Scene Logger.

## Player commands

The package follows **[Volund's SceneSys](https://github.com/volundmush/mushcode)** command
surface — a unified `+scene/<switch>` verb plus a standalone `+pot`. Default permission:
anyone may create/schedule (and becomes owner); owner-only for lifecycle/management; authors
edit poses. Wizards, and anyone allowed the package's `scene.manage` permission, may do both on
any scene; `scene.close` lets staff finish one. Every switch takes a unique prefix (`+scene/lis` is `+scene/list`), and a
line no verb matches answers with the switch's usage line instead of `Huh?`.

| Command | Does |
|---|---|
| `+scene` · `/list` | list active scenes |
| `+scene <id or title>` · `+event <id>` | scene details card; a title, or its start, finds the scene too |
| `+scene/old` &nbsp;·&nbsp; `+scene/mine` | finished &nbsp;·&nbsp; your scenes |
| `+scene/create <title>` | create + focus (active by default) |
| `+scene/start [<id>]` · `/pause [<id>][=<when>]` · `/finish` | lifecycle; a paused scene leaves the live list for the schedule until started again; a finished or cancelled scene stays that way |
| `+scene/join <id>` · `/leave` | membership + focus |
| `+scene/tag <id>` · `/untag <id>` (`/rsvp` · `/unrsvp`) | RSVP |
| `+scene/activate <id>` · `/deactivate` | resume / pause recording without leaving |
| `+scene/as [<persona>]` | display persona for your future poses; bare, or your own name, clears it |
| `+scene/title [<id>=]<text>` · `/pitch [<id>=]<text>` | rename it · set the scene blurb (the focused scene, or the one named) |
| `+scene/public [<id>]` · `/private [<id>]` | visibility |
| `+scene/pose <id>=<text>` · `/say` · `/semipose` · `/emit` · `/ooc` | pose into a named scene (what the portal's compose box sends); joins and focuses you on it, and the room sees it framed as a captured pose |
| `+scene/recall [<n>]` · `/recall <id>=[<n>]` · `/log [<id>]` | print the last `<n>` poses, each under a rule naming its poser and id, and `<OOC>` before an out-of-character line; bare, ``DATA`CONFIG`RECALL-ROUNDS`` rounds (`+config scene/recall-rounds`) of the cast (2 × its size). Named, any scene you can read; `/log` the whole of it |
| `+scene/edit <id>=<before>^^^<after>` · `/rewrite <id>=<text>` | fix a typo in your pose · replace its text |
| `+scene/undo <id>` · `/redo <id>` · `/delete <id>` · `/move <id>=<after>` | pose management |
| `+scene/info <id>` | the scene's card: pitch, where, status, cast, members with roles, who may watch (same as `+scene <id>`) |
| `+scene/schedule <title>=<when>` | schedule a roomless future scene; `<when>` is a date `convtime()` reads (`2026-12-31 20:00`), `+<n>h`/`+<n>d`/`+<n>w` from now, or epoch seconds, and must be to come |
| `+scene/reschedule <id>=<when>` · `/unschedule <id>` (`/cancel`) · `/upcoming [<days>\|all]` (`+scenes`, `+events`) | manage / list scheduled, grouped by day, 30 days ahead unless told (``DATA`CONFIG`SCHEDULE-DAYS``); rescheduling a running scene pauses it, cancelling marks a scene that has not started `cancelled` |
| `+pot` | pose tracker (turn order for the focused scene, idle time and the length of each last pose) |

**Reads are functions, writes are commands.** Reads use the `scene…()` functions
(`scenewhere`, `scenefocus`, `scene`, `sceneposes`, `scenepose`, `scenemember`, `scenecast`, …);
all writes go through wizard-only `@scene/*` commands that the WIZARD Logger satisfies, so the
package works **without** enabling `function_side_effects`. Object arguments (rooms, players)
resolve through the engine's **`LocateService`**, so `here`, `me`, player names, `*name`, and
dbrefs all work just as they do for every other engine function.

## Temp rooms (optional extension)

A scene can have its own staging room: `@dig` it, set the `SCENE_ROOM` flag (symbol `S`),
`@tel` joiners in, and `@destroy` it after a grace period on `/finish`. This is **not** shipped
by default — add a `+scene/create/temp` verb to the Logger via the package manager if you want
it; the `DATA\`TEMP_*` knobs are reserved for that use.

## Verifying

```mush
+scene/create Test
@emit A figure steps from the shadows.
think sceneposes(scenewhere(loc(me)))    @@ -> a pose id (capture worked)
think scenepose(scenewhere(loc(me)), first(sceneposes(scenewhere(loc(me)))), content)
```

The portal `/scenes` browser and `/scenes/{id}/live` feed read the same data over
`/api/scenes`; the live page receives each new pose over `game.scene.{id}` as it is captured.
