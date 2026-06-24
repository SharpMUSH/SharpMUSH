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
> never see the pose text. The portal's live composer sends a normal `POSE`/`SAY`/`SEMIPOSE`
> through `GameHub.SendCommand`, so the **same** hook fires — there is **one** stored pose,
> rendered both to the room and over `game.scene.{id}` (no double-capture, no echo loop).

## Policy knobs (``DATA`*``)

There is intentionally **no `SceneOptions` config category** — every knob is policy, set as a
``&DATA`*`` attribute on the Scene Logger and read by the verbs (one place, not two). The
package ships sensible defaults; the most notable is **``DATA`DEFAULT_STATUS=active``**, so
`+scene/create` yields an immediately-capturing scene (set it to `new`/`scheduled` to stage a
scene that only logs after `+scene/start`). See the ``DATA`*`` block in `package.yaml` for the
full set (`CAPTURE`, `DEFAULT_STATUS`, `DEFAULT_PUBLIC`, `MAX_RECENT`, `STATUSES`, `TEMP_*`,
`SHARE_OWNER`) and its current defaults.

## Player commands

The package follows **[Volund's SceneSys](https://github.com/volundmush/mushcode)** command
surface — a unified `+scene/<switch>` verb plus a standalone `+pot`. Default permission:
anyone may create/schedule (and becomes owner); owner-only for lifecycle/management; authors
edit their own poses.

| Command | Does |
|---|---|
| `+scene` | list active scenes |
| `+scene <id>` | scene details card |
| `+scene/old` &nbsp;·&nbsp; `+scene/mine` | finished &nbsp;·&nbsp; your scenes |
| `+scene/create <title>` | create + focus (active by default) |
| `+scene/start` · `/pause` · `/finish` | lifecycle |
| `+scene/join <id>` · `/leave` | membership + focus |
| `+scene/tag <id>` · `/untag <id>` | RSVP |
| `+scene/activate <id>` · `/deactivate` | resume / pause recording without leaving |
| `+scene/as <persona>` | display persona for your future poses |
| `+scene/pitch <text>` | set the scene blurb |
| `+scene/public` · `/private` | visibility |
| `+scene/recall [<n>]` | print the last `<n>` poses |
| `+scene/edit <id>=<before>^^^<after>` | fix a typo in your pose |
| `+scene/undo <id>` · `/redo <id>` · `/delete <id>` · `/move <id>=<after>` | pose management |
| `+scene/who <id>` | cast list |
| `+scene/schedule <title>=<when>` | schedule a roomless future scene |
| `+scene/reschedule <id>=<when>` · `/unschedule <id>` · `/upcoming` | manage / list scheduled |
| `+pot` | pose tracker (turn order for the focused scene) |

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
