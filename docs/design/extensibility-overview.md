# Extensibility Overview — how to extend SharpMUSH

SharpMUSH can be extended at **three layers**, from "edit C# in the engine" to "drop
a softcode package in at runtime." This page is the map: what each layer is, when to
reach for it, and how they compose. Each layer has its own detailed design doc — this
one links them together.

```
                        ┌─────────────────────────────────────────────┐
   game policy ──────►  │  Softcode packages  (YAML + MUSHcode)         │   runtime, no C#
                        │  @function · @ainstall/@aupdate · bundled pkgs │
                        └───────────────▲─────────────────────────────┘
                                        │ uses the primitives/commands below
                        ┌───────────────┴─────────────────────────────┐
   compiled features ►  │  C# DLL plugins   (external assemblies)       │   runtime-loaded
                        │  commands/functions · services · migrations · │
                        │  flags · bridge legs · hooks · unload         │
                        └───────────────▲─────────────────────────────┘
                                        │ same attributes + contracts as in-tree
                        ┌───────────────┴─────────────────────────────┐
   engine primitives ►  │  In-tree engine   (SharpMUSH.Implementation)  │   compile-time
                        │  [SharpCommand]/[SharpFunction] · migrations · │
                        │  flags · ISharpDatabase · the parser           │
                        └─────────────────────────────────────────────┘
```

## Layer 1 — engine primitives (compile-time C#)

The base layer: `[SharpCommand]`/`[SharpFunction]` static methods, DB migrations, flags,
`ISharpDatabase`, and the MUSHcode parser, compiled into `SharpMUSH.Implementation` and
discovered at **compile time** by source generators. This is where genuinely core,
always-present behavior lives. Changing it means recompiling the server.

## Layer 2 — C# DLL plugins (compiled, runtime-loaded)

A plugin is an ordinary `net10.0` assembly dropped into `plugins/` (or distributed by the
package manager) that contributes compiled C# **without forking the engine**. Authoring is
identical to in-tree code — the same `[SharpCommand]`/`[SharpFunction]` attributes, surfaced
through `PluginBase` — and plugins can also contribute DI services, DB migrations, engine
flags, NATS-bridge legs, and **extension hooks** (the C# analog of softcode `@hook`).
Command/function/hook-only plugins can be **hot-unloaded**; plugins that touch the DI
container / DB / bridge are load-once. Load order is resolved from declared dependencies.

Reach for a plugin when the extension needs real C# (new storage, services, performance-
sensitive logic, integration with .NET libraries) but shouldn't live in the core engine.

→ Full design: [`plugin-system.md`](./plugin-system.md). Authoring: [`../guides/writing-a-plugin.md`](../guides/writing-a-plugin.md).

## Layer 3 — softcode packages (MUSHcode, runtime)

The package manager installs **YAML packages** of game objects + attributes (and, with
`@function`, global softcode functions) at runtime, with `@ainstall`/`@aupdate` lifecycle
hooks and three-way-merge upgrades. Default packages are bundled and auto-installed at boot
(`http-handler`, `profile-handler`, `common-functions`, `scene`). This is the home for
**game policy** — who may do what, formatting, capture rules — written in MUSHcode by
admins, no C# and no recompile.

Reach for a softcode package when the extension is policy or content that a game owner
should be able to read, fork, and edit live.

→ See the package manager (Area 20) docs and the bundled packages under `examples/packages/`.

## Choosing a layer

| You want to… | Use |
|---|---|
| add/replace a global command or function in pure MUSHcode | **Softcode** (`@function`, a package) |
| express game **policy** (permissions, formatting, capture) editable live | **Softcode package** |
| ship compiled C# (new storage, services, .NET integration) without forking the engine | **C# plugin** (Layer 2) |
| intercept command execution / react to connection/object lifecycle in C# | **C# plugin hooks** |
| change always-on core engine behavior | **In-tree** (Layer 1) |

The guiding split is **mechanism vs policy**: C# (engine or plugin) ships *mechanism* —
primitives, storage, wiring; softcode owns *policy* — the rules a particular game chooses.

## Worked example: the Scene system spans two layers

Scene is the reference case because it uses **both** extension layers, illustrating the split:

- **As a C# plugin** — `SharpMUSH.Plugins.Scene` contributes the wizard `@SCENE` command,
  the `scene…()` functions, the scene DB migration, the `SCENE_ROOM` flag, and the
  `game.scene.*` realtime leg, via the plugin contribution surfaces. (`ISceneService` graph
  *storage* stays in the DB providers — it can't live in a collectible DLL.) This is the
  **mechanism**: store a pose, broadcast an event, gate `@SCENE` to wizards.
- **As a softcode package** — the bundled `scene` package ships the `#SCENELOGGER`-style
  policy: an `@hook/override` that *captures* poses, the `+scene/*` player verbs, temp-room
  orchestration, and permission rules — all editable by the game owner. This is the
  **policy**: *when* a pose is captured and *who* may start a scene.

The same unchanged scene test suite passes whether Scene runs in-tree or as a loaded plugin
DLL — the proof the layering is a clean *move*, not a *rewrite*.

→ Scene design: [`scene-system.md`](./scene-system.md). Bootstrap softcode: [`../setup/scene-bootstrap.md`](../setup/scene-bootstrap.md).
