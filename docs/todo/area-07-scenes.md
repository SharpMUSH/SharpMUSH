# Area 7: Scene System — TODO (Reconciled 2026-06-20)

> **2026-06-20 status:** Phases 0–7 are **shipped**. The system is extracted into
> `SharpMUSH.Plugins.Scene` (commands/functions/migration/flag/bridge) with
> tri-provider graph storage, realtime, and full portal UI; the scene suite is
> green on all three providers. Two design deviations from the plan below, now
> reflected in the boxes: **(a)** there is **no `InMemorySceneService`** — the
> three DB providers implement `ISceneService` directly and the WASM client reads
> over the server API (Phase 1 reframed); **(b)** the member model is
> `SceneMember`, not `SceneMemberEdge`. Remaining work is the optional temp-room
> softcode extension (Phase 6, not in the 1.0 package) and a display audit pass.

Reconciled to the **graph-native, mechanism/policy** design in
`docs/design/scene-system.md`. The engine ships wizard-only primitives (`@SCENE`
+ `scene…` functions); capture/permission/formatting/temp-rooms are **softcode**.
POSE/SAY/SEMIPOSE are **never patched**; capture is `@hook/override`. Storage is
a named graph `graph_sharp_sys_scene` (vertices `node_sharp_sys_scene_*`, edges
`edge_sharp_sys_scene_*`); object references are **edges + `Name` snapshots**;
pose order is a `pose_next` list; pose content is versioned in `pose_edits`;
**no `ActRole`** (pose `ShowAsName`), **no `SCENE`*` object attributes** (member
edge + `scenewhere()`). Each phase is independently shippable and lights up one
plugin seam.

## Pre-Implementation
- [x] Decisions 7.1–7.6 amended & **confirmed by owner** (2026-06-19)
- [x] Locked: graph schema + `sharp_sys_scene` namespace; ObjId→edges + keep
      `Name` snapshots (display uses snapshot, edge offers live link); pose order
      = `pose_next` list; versioned `pose_edits` + `current_edit` pointer; no
      `ActRole` (opaque `ShowAsName`); no `SCENE`*` attributes (member edge
      `isCurrent`/`showAs` + `scenewhere()`); free-string `Status`
      (`new`/`active`/`paused`/`finished`); `SCENE_ROOM` flag symbol `S`; temp
      rooms + scheduling activation + recycle janitor are **softcode**; RSVP =
      `attending` member role; `Close`/`Open` re-sign = clean break; UTC millis;
      function names no-underscore `scene…` (verb-form writes)
- [ ] Remaining tasks (not decisions): `AuthorName`/`ShowAsName` display audit
      (Phase 5); confirm `@wait`/cron + building-command set (`@dig`/`@tel`/
      `@destroy`/`lcon`) for the **optional** temp-room softcode (Phase 6, not in
      the shipped 1.0 package — documented in `docs/setup/scene-bootstrap.md` §4)

## Phase 0 — Models & contracts (graph-aware) — ✅ shipped
- [x] `SharpMUSH.Library/Models/Scene/`: `Scene`, `ScenePose`, `ScenePoseEdit`,
      `ScenePlot` records (first-class fields + `Meta` maps + `Name` snapshots,
      **no `*ObjId` strings**), `SceneEventMessage` (Portal), `SceneMember`
      (role/showAs/isCurrent/grantedAt projection — named `SceneMember`, not
      `SceneMemberEdge`)
- [x] All timestamps `long` UTC-millis; `Status` free string; `Content` plain +
      `Markup` (no `RenderedHtml`)
- [x] **Clean break:** legacy `SceneArchive`/`SceneMessage`/`SceneMessageType` +
      `PostMessageAsync` removed (verified gone — no compat type); callers updated
- **Ships:** type contracts. **Tests:** compile-only; pages still build.

## Phase 1 — `ISceneService` surface — ✅ shipped (no in-memory impl, by design)
- [x] Define `ISceneService` with the full primitive set (dbref args; service
      manages edges + name snapshots): create (roomless allowed), set (known
      keys → field/edge, else `Meta`), addpose, setpose, editpose (versioned),
      undo/redo (move `current_edit`), move (`pose_next` re-link), delete
      (soft), addmember/unmember, setfocus, showas, plot ops; reads `scene`,
      `scenelist`, `scenewhere`, `sceneposes`, `scenepose`, `sceneedits`,
      `scenemembers`, `scenemember`, `scenefocus`, `scenetags`, `scenecast`
      (`SharpMUSH.Library/Services/Interfaces/ISceneService.cs`)
- [x] ~~`InMemorySceneService`~~ **dropped by design** — there is no in-memory
      implementation. The three DB providers implement `ISceneService` directly
      (Phase 3) and the WASM client reads scene data over the server API. The
      Phase-1 graph-mechanism behaviours (`pose_next` chain, `current_edit`
      undo/redo, soft-delete, `isCurrent` single-current, `scenewhere`, roomless
      create, `scheduled` filters, name snapshots) are tested against the real
      providers in Phase 3 instead of an in-memory oracle.
- **Ships:** complete mechanism contract, validated via the DB providers (P3).

## Phase 2 — `SCENE_ROOM` flag + wizard `@SCENE` + `scene…` functions — ✅ shipped
- [x] `SCENE_ROOM` (`typesRoom`, wizard set/unset, informational, **symbol `S`**)
      contributed by the plugin via `IFlagSource` (`ScenePlugin.cs`) — seam #1b —
      instead of being seeded in the engine's flag seeders
- [x] `Commands/SceneCommandModule.cs` (`@SCENE`, wizard-gated) + switch dispatch
      and handler classes `SceneRead`/`SceneWrite`/`ScenePoseHandlers`/
      `SceneMemberHandlers`/`ScenePlotHandlers`; switches LIST/GET/CREATE/SET/
      ADDPOSE/SETPOSE/EDITPOSE/UNDO/REDO/MOVE/DELETE/MEMBER/UNMEMBER/FOCUS/SHOWAS/
      PLOT/LINK/UNLINK/NOEVAL (`SharpMUSH.Plugins.Scene/Commands/`)
- [x] `SceneBroadcast.cs` shared publish helper (used by command + function)
- [x] `Functions/SceneFunctions.cs`: reads (`Regular`) + writes
      (`WizardOnly|HasSideFX`, verb-form, dbref args, return id/value):
      `scenecreate`/`sceneset`/`sceneaddpose`/`scenesetpose`/`sceneeditpose`/
      `sceneundo`/`sceneredo`/`scenemovepose`/`scenedelpose`/`sceneaddmember`/
      `sceneunmember`/`scenesetfocus`/`sceneshowas`/`sceneplot` + all read fns
- **Ships:** softcode drives scenes end-to-end on durable storage; OVERRIDE
  capture works.
- **Test matrix:** wizard gate denies non-wizards; read-function visibility
  (`#-1 PERMISSION` non-members); side-fx guard + inline returns; `@scene/set`
  routes known keys vs `Meta`; `@scene/addpose`→`scenepose` reads from
  `current_edit`; worked-example integration (`scenecreate`→`SID`,
  `@hook/override POSE`→`sceneaddpose` reading `scenewhere`/`scenefocus`/
  `scenemember(...,showas)`); `hasflag(<room>,SCENE_ROOM)` after softcode `@set`.

## Phase 3 — DB-backed `ISceneService` across 3 providers + migrations — ✅ shipped
- [x] `DatabaseConstants.cs`: the `node_sharp_sys_scene_*`, `edge_sharp_sys_scene_*`,
      `graph_sharp_sys_scene` names (`SharpScenes`/`SharpScenePoses`/
      `SharpScenePoseEdits`/`SharpScenePlots` + the edge-type set)
- [x] **ArangoDB** `Migration_AddScenes : IArangoMigration` (in the plugin, via
      `IMigrationSource`): vertex doc collections; an **edge collection per edge
      type**; the named graph with edge definitions (incl. cross-collection edges
      into core collections); indexes on `Scene.Status`/`ScheduledFor`/`IsPublic`
- [x] `ArangoDatabase.Scene.cs` / `MemgraphDatabase.Scene.cs` /
      `SurrealDatabase.Scene.cs` partials implementing `ISceneService` —
      graph traversals (not FK scans)
- [x] Memgraph: labels + relationship types + indexes (auto-commit DDL).
      Surreal: tables + `RELATE` edges; `*DbRecord` **verbatim camelCase** (CBOR
      gotcha); `SCENE_ROOM` contributed via `IFlagSource` (all providers)
- [x] Server `Startup.cs`: `ISceneService` resolves from `ISharpDatabase` (the
      provider tri-cast). **`Client/Program.cs` registers no `ISceneService`** —
      the WASM client reads scene data over the server API
- **Ships:** durable scenes + edges + edits on the default provider, incl.
  1-based scene/pose ids across all three providers (this branch).
- **Test matrix (all 3 via Podman):** per-method parity; `pose_next` move; edit
  versioning + undo/redo pointer; soft-delete; member `isCurrent`; `scenewhere`
  via `in_room`; **object-edge + `Name` snapshot round-trip, incl. target-delete
  → edge gone, snapshot remains**; Surreal CBOR round-trips `Tags`/`Meta`/edges;
  migration idempotency; `SCENE_ROOM` parity; UTC-ms boundary. Phase-1 in-memory
  tests re-run as the oracle.

## Phase 4 — Realtime `game.scene.{id}` leg — ✅ shipped
- [x] Scene subscription moved into the plugin as an `IBridgeSubscriptionSource`
      (`game.scene.*`), enumerated by `NatsBridgeService` instead of a hard-coded
      `Task.WhenAll` — seam #6
- [x] `GameHub`: `ReceiveSceneMessage` on `IGameHubClient` + `SendToSceneAsync`
      (feeds the `scene:{id}` groups)
- [x] Client `ConnectionStateService.OnSceneEventReceived` wired to the
      `ReceiveSceneMessage` hub event
- [x] `@SCENE` arms + side-effect functions both route through `SceneBroadcast`
- **Ships:** live poses in subscribed clients.
- **Test matrix:** publish → subject `game.scene.{id}`; bridge forwarding (mock
  `IHubContext`); client handler fires; existing `NatsBridgeServiceTests` green.

## Phase 5 — Portal UI + web pose-authoring + tag filtering — ✅ shipped (audit open)
- [x] The five surfaces exist on the new functions/`ISceneService`: `SceneDetail`,
      `SceneLive`, `Scenes`, `ScenesActive` (`Client/Pages/`) + `ActiveSceneWidget`
      (`Client/Components/Widgets/`)
- [x] `SceneDetail`: render `Markup` client-side; edited badge; struck
      `IsDeleted` (owner); dynamic tag chips from `scenetags`/distinct `Tags`
- [x] `SceneLive`: `JoinScene` + `OnSceneEventReceived` patch by pose id; pose
      editor submits a normal **POSE/SAY/SEMIPOSE** (never `@emit`) via
      `GameHub.SendCommand` (no `ISceneService` write, no optimistic insert);
      legacy direct write removed
- [x] `Scenes`/`ScenesActive`: plot grouping, cohort + RSVP counts, temp badge;
      `+schedule`-style agenda surface (sorted by `ScheduledFor`)
- [x] Client models use `long` UTC-millis
- [ ] **OPEN:** `AuthorName`/`ShowAsName` display audit — confirm every surface
      shows `ShowAsName` (fallback `AuthorName`) and never keys *logic* off a
      display name
- **Ships:** full portal scene experience incl. web authoring.
- **Test matrix (bUnit):** ordered render; `ShowAsName` display; `IsDeleted`
  hidden for non-owners; tag chips filter; `SceneLive` single-render of own pose
  (no double from room emit + feed); **editor calls `SendCommand` with POSE/SAY/
  SEMIPOSE, never `ISceneService`/`@emit`**; `long` timestamp binding.

## Phase 6 — `#SCENELOGGER` softcode bootstrap
- [x] **No `SceneOptions` config** — dropped by design (every knob is softcode
      policy with zero C# consumers; the bootstrap hardcodes its own policy via
      `&conf.* #SCENELOGGER` attributes). Keeps the mechanism/policy split clean.
- [x] `docs/setup/scene-bootstrap.md` — the **WIZARD** Scene Logger:
      `@hook/override POSE/SAY/SEMIPOSE` (and `@EMIT`, this branch) capture
      (reproduce-emit + `sceneaddpose` via `scenewhere`/`scenefocus`/
      `scenemember(...,showas)`); `+scene/*` verbs (`create`,`schedule`,`start`,
      `pause`,`finish`,`showas`,`edit`,`undo`,`redo`,`delete`,`move`,`join`,
      `leave`,`tag`/`untag` RSVP, meta verbs, `+scene`/`list`/`log`/`who`,
      `+schedule`/`+scenes`, `plot/*`); owner-only via
      `@assert strmatch(scene(<id>,owner),%#)`; logger-must-be-WIZARD documented.
      This branch: capture keys off `loc(%#)` and the logger parks in master room
      `#2` at AINSTALL
- [ ] **OPTIONAL (not in shipped 1.0 package):** the full temp-room softcode
      lifecycle (`@dig`+`@set SCENE_ROOM`+`scenecreate`; occupant-safe
      `lcon()`-evacuate-then-`@destroy` recycle janitor) — documented as an
      opt-in extension in `scene-bootstrap.md` §4, not packaged by default
- **Ships:** canonical wiring docs + the packaged Scene Logger.
- **Test matrix:** config binds; `Scene` category generated; stale generator does
  not drop it.

## Phase 7 — Plugin-seam hardening → **extraction realized** (Scene is now a plugin)
The seams below were proposed here, then **built and consumed for real** when Scene
was extracted into `SharpMUSH.Plugins.Scene` (Phase 5 of the plugin framework).
- [x] `IBridgeSubscription` registry — built as the framework's `IBridgeSubscriptionSource`;
      `NatsBridgeService` now enumerates registered sources instead of a hard-coded
      `Task.WhenAll`, and `ScenePlugin` contributes the `game.scene.*` leg. (seam #6)
- [x] `IFlagContribution` — built as `IFlagSource` + `PluginFlag`; `ScenePlugin` ships
      `SCENE_ROOM` without the engine seeding it. (seam #1b)
- [x] `SceneEventMessage` + the named-graph edges referencing core collections are the
      documented coupling points; the realtime contract is frozen.
- **Shipped:** `SharpMUSH.Plugins.Scene` (commands/functions via `ICommandSource`/
      `IFunctionSource`, migration via `IMigrationSource`, flag via `IFlagSource`, bridge
      via `IBridgeSubscriptionSource`); `ISceneService` graph storage stays in the
      providers. The unchanged 29-test scene suite passes with Scene as a loaded plugin —
      see `docs/design/plugin-system.md` §"Phase 5".

## Cross-Phase Test Matrix Summary

> Note: the **In-Memory (TUnit)** column reflects the original plan's
> `InMemorySceneService` oracle, which was dropped. Those `P1` mechanism concerns
> are now covered directly against the three real providers (the `Arango`/
> `Memgraph`/`Surreal` columns) via the integration scene suite.

| Concern | In-Memory (TUnit) | Arango | Memgraph | Surreal | bUnit |
|---|---|---|---|---|---|
| Service-method parity | P1 | P3 | P3 | P3 | — |
| `pose_next` order + move re-link | P1 | P3 | P3 | P3 | — |
| Versioned edits + undo/redo pointer | P1 | P3 | P3 | P3 | — |
| Soft-delete keeps slot | P1 | P3 | P3 | P3 | — |
| Object edge + `Name` snapshot (incl. target delete) | P1 | P3 | P3 | P3 | P5 (display) |
| Member edge `isCurrent`/`showAs`; `scenefocus`/`scenewhere` | P1 | P3 | P3 | P3 | — |
| Roomless create + scheduled window/sort | P1 | P3 | P3 | P3 | — |
| `SCENE_ROOM` (`S`) seed + `hasflag` parity | — | P3 | P3 | P3 | — |
| `@SCENE` wizard gate + switches | P2 | — | — | — | — |
| Functions + side-fx guard + inline returns | P2 | — | — | — | — |
| OVERRIDE capture (text via `$`-match, focus==room) | P2 | — | — | — | — |
| Migration idempotency + named graph | — | P3 | P3 | P3 | — |
| Realtime publish/forward | P4 | — | — | — | — |
| Web pose → SendCommand (no `@emit`, no double-capture) | — | — | — | — | P5 |
| Pages + tag chips + ShowAsName + live patch | — | — | — | — | P5 |
| `long` UTC-ms contract | P1 | P3 | P3 | P3 | P5 |
| Config category | — (dropped: no `SceneOptions`) | — | — | — | — |
| Temp-room softcode (dig/recycle occupant-safe) | — | — | — | — | — (P6 docs) |
