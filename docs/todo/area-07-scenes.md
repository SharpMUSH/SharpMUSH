# Area 7: Scene System — TODO (Reconciled 2026-06-19)

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
      `@destroy`/`lcon`) for the softcode bootstrap (Phase 6)

## Phase 0 — Models & contracts (graph-aware)
- [ ] `SharpMUSH.Library/Models/Scene/`: `Scene`, `ScenePose`, `ScenePoseEdit`,
      `ScenePlot` records (first-class fields + `Meta` maps + `Name` snapshots,
      **no `*ObjId` strings**), `SceneEventMessage` (Portal), `SceneMemberEdge`
      (role/showAs/isCurrent/grantedAt projection)
- [ ] All timestamps `long` UTC-millis; `Status` free string; `Content` plain +
      `Markup` (no `RenderedHtml`)
- [ ] **Clean break:** remove the legacy `SceneArchive`/`SceneMessage`/
      `SceneMessageType` + `PostMessageAsync` (no permanent compat type); update
      every caller
- **Ships:** type contracts. **Tests:** compile-only; pages still build.

## Phase 1 — `ISceneService` surface + `InMemorySceneService`
- [ ] Define `ISceneService` with the full primitive set (dbref args; service
      manages edges + name snapshots): create (roomless allowed), set (known
      keys → field/edge, else `Meta`), addpose, setpose, editpose (versioned),
      undo/redo (move `current_edit`), move (`pose_next` re-link), delete
      (soft), addmember/unmember, setfocus, showas, plot ops; reads `scene`,
      `scenelist`, `scenewhere`, `sceneposes`, `scenepose`, `sceneedits`,
      `scenemembers`, `scenemember`, `scenefocus`, `scenetags`, `scenecast`
- [ ] Implement all in `InMemorySceneService` (graph emulated: `pose_next`
      chain + first/last; `current_edit`/edit chain + undo/redo pointer;
      soft-delete keeps slot; member edges incl. `isCurrent` single-current
      invariant; `scenewhere` = in-room active; `scenecreate` roomless;
      `Status`/`ScheduledFor` filters; name snapshot on edge create)
- **Ships:** complete mechanism contract, testable in isolation (seam #5 half).
- **Test matrix (TUnit, in-memory):** append/last-pose; **move re-link**
  (front/middle/end); **undo→redo→edit-truncates-forward**; soft-delete keeps
  chain; member `isCurrent` single-current; `scenefocus`/`scenewhere`;
  roomless create + later room-bind via `set room=`; `scheduled` window/sort;
  name snapshot survives a simulated target deletion (edge gone, name remains).

## Phase 2 — `SCENE_ROOM` flag + wizard `@SCENE` + `scene…` functions
- [ ] Seed `SCENE_ROOM` (`typesRoom`, wizard set/unset, informational, **symbol
      `S`**) in `Migration_CreateDatabase.CreateInitialFlags` + Memgraph +
      Surreal flag seeders — seam #1b
- [ ] `Commands/SceneCommand/SceneCommand.cs` (`[SharpCommand(Name="@SCENE",
      CommandLock="FLAG^WIZARD")]` + `IsWizard()` + switch dispatch) and handler
      classes: scene-scoped (`create`,`set`,`addpose`,`member`,`unmember`,
      `focus`,`showas`,`plot`,`list`,`get`,bare) take `<sceneId>`; pose-scoped
      (`setpose`,`editpose`,`undo`,`redo`,`move`,`delete`) take `<poseId>`
- [ ] `SceneBroadcast.cs` shared publish helper (used by command + function)
- [ ] `Functions/SceneFunctions.cs`: reads (`Regular`) + writes
      (`WizardOnly|HasSideFX`, verb-form, dbref args, return id/value):
      `scenecreate`/`sceneset`/`sceneaddpose`/`scenesetpose`/`sceneeditpose`/
      `sceneundo`/`sceneredo`/`scenemovepose`/`scenedelpose`/`sceneaddmember`/
      `sceneunmember`/`scenesetfocus`/`sceneshowas`/`sceneplot`
- **Ships:** softcode drives scenes end-to-end on in-memory storage; OVERRIDE
  capture works.
- **Test matrix:** wizard gate denies non-wizards; read-function visibility
  (`#-1 PERMISSION` non-members); side-fx guard + inline returns; `@scene/set`
  routes known keys vs `Meta`; `@scene/addpose`→`scenepose` reads from
  `current_edit`; worked-example integration (`scenecreate`→`SID`,
  `@hook/override POSE`→`sceneaddpose` reading `scenewhere`/`scenefocus`/
  `scenemember(...,showas)`); `hasflag(<room>,SCENE_ROOM)` after softcode `@set`.

## Phase 3 — DB-backed `ISceneService` across 3 providers + migrations
- [ ] `DatabaseConstants.cs`: the `node_sharp_sys_scene_*`, `edge_sharp_sys_scene_*`,
      `graph_sharp_sys_scene` names
- [ ] **ArangoDB** `Migration_AddScenes : IArangoMigration`: vertex doc
      collections; an **edge collection per edge type**; the named graph with
      edge definitions (incl. cross-collection edges into `node_rooms`/
      `node_players`/`node_objects`); indexes on `Scene.Status`/`ScheduledFor`/
      `IsPublic`
- [ ] `ArangoDatabase.Scene.cs` / `MemgraphDatabase.Scene.cs` /
      `SurrealDatabase.Scene.cs` partials implementing `ISceneService` —
      **NET-NEW files**, graph traversals (not FK scans)
- [ ] Memgraph: labels + relationship types + indexes (auto-commit DDL).
      Surreal: tables + `RELATE` edges; `*DbRecord` **verbatim camelCase** (CBOR
      gotcha); confirm `SCENE_ROOM` seed in all three flag seeders
- [ ] Server `Startup.cs:257`: replace in-memory reg with the `ISceneService`
      tri-cast (`IWikiService` precedent at `:242`). **`Client/Program.cs:33`
      STAYS `InMemorySceneService`** (WASM no DB)
- **Ships:** durable scenes + edges + edits on the default provider.
- **Test matrix (all 3 via Podman):** per-method parity; `pose_next` move; edit
  versioning + undo/redo pointer; soft-delete; member `isCurrent`; `scenewhere`
  via `in_room`; **object-edge + `Name` snapshot round-trip, incl. target-delete
  → edge gone, snapshot remains**; Surreal CBOR round-trips `Tags`/`Meta`/edges;
  migration idempotency; `SCENE_ROOM` parity; UTC-ms boundary. Phase-1 in-memory
  tests re-run as the oracle.

## Phase 4 — Realtime `game.scene.{id}` leg
- [ ] `NatsBridgeService.SubscribeSceneAsync` added to `Task.WhenAll`
- [ ] `GameHub`: `ReceiveSceneMessage` on `IGameHubClient` + `SendToSceneAsync`
      (feeds the existing-but-unpopulated `scene:{id}` groups)
- [ ] Client `ConnectionStateService.OnSceneEventReceived` + the
      `On(string, Action<SceneEventMessage>)` overload on `IGameHubConnection`/
      `GameHubConnectionFactory`
- [ ] Confirm `@SCENE` arms + side-effect functions both route through
      `SceneBroadcast`
- **Ships:** live poses in subscribed clients.
- **Test matrix:** publish → subject `game.scene.{id}`; bridge forwarding (mock
  `IHubContext`); client handler fires; existing `NatsBridgeServiceTests` green.

## Phase 5 — Portal UI + web pose-authoring + tag filtering
- [ ] Migrate the five surfaces (4 pages + `ActiveSceneWidget`) off the legacy
      models onto the new functions/`ISceneService`
- [ ] `SceneDetail`: render `Markup` client-side; display **`ShowAsName`
      (fallback `AuthorName`)** — never key logic off a display name; edited
      badge (edit count); struck `IsDeleted` (owner); dynamic tag chips from
      `scenetags`/distinct `Tags`
- [ ] `SceneLive`: `JoinScene` + `OnSceneEventReceived` patch by pose id;
      **REMOVE the legacy direct write at `SceneLive.razor:148`**; pose editor
      submits a normal **POSE/SAY/SEMIPOSE** (never `@emit`) via
      `GameHub.SendCommand` (no `ISceneService` write, no optimistic insert)
- [ ] `Scenes`/`ScenesActive`: plot grouping, cohort + RSVP counts, temp badge;
      a `+schedule`-style agenda surface (sorted by `ScheduledFor`)
- [ ] Client models use `long` UTC-millis
- **Ships:** full portal scene experience incl. web authoring.
- **Test matrix (bUnit):** ordered render; `ShowAsName` display; `IsDeleted`
  hidden for non-owners; tag chips filter; `SceneLive` single-render of own pose
  (no double from room emit + feed); **editor calls `SendCommand` with POSE/SAY/
  SEMIPOSE, never `ISceneService`/`@emit`**; `long` timestamp binding.

## Phase 6 — `SceneOptions` config + `#SCENELOGGER` softcode bootstrap
- [ ] `SceneOptions.cs` (advisory: capture toggle, logger object, default
      status/public, temp-room knobs, known-status/tag UI hints, share-requires-
      owner) + `SharpMUSHOptions.Scene` (generator re-runs)
- [ ] `docs/setup/scene-bootstrap.md` — the **WIZARD** `#SCENELOGGER`:
      `@hook/override POSE/SAY/SEMIPOSE` capture (reproduce-emit + `sceneaddpose`
      via `scenewhere`/`scenefocus`/`scenemember(...,showas)`); `+scene/*` verbs
      (`create`,`create/temp`,`schedule`,`reschedule`,`start`,`pause`,`finish`,
      `showas`,`edit <pose>=<find>^^^<replace>`,`undo`,`redo`,`delete`,`move`,
      `join`,`leave`,`invite`,`watch`,`boot`,`tag`/`untag` RSVP,`share`,`unshare`,
      `private`,`public`,meta verbs, `+scene`/`list`/`log`/`recap`/`who`,
      `+schedule`/`+scenes`, `plot/*`); **entire temp-room lifecycle in softcode**
      (`@dig`+`@set SCENE_ROOM FLOATING`+`scenecreate`; occupant-safe
      `lcon()`-evacuate-then-`@destroy` recycle janitor); owner-only via
      `@assert strmatch(scene(<id>,owner),%#)`; `/leave` scrubs no-pose members
      (`sceneunmember`) else clears focus; document logger-must-be-WIZARD
- **Ships:** admin-tunable config + canonical wiring docs.
- **Test matrix:** config binds; `Scene` category generated; stale generator does
  not drop it.

## Phase 7 — Plugin-seam hardening (pre-extraction, design only)
- [ ] Propose `IBridgeSubscription` registry (replace hard-coded
      `NatsBridgeService.Task.WhenAll`) — seam #6 blocker
- [ ] Propose `IFlagContribution` (ship `SCENE_ROOM` without editing core flag
      seeding) — seam #1b
- [ ] Confirm `SceneEventMessage` + the named-graph edge definitions referencing
      core collections (`node_rooms`/`node_players`/`node_objects`) are the
      documented extraction coupling points; freeze the realtime contract
- **Ships:** extraction-readiness note + contribution-inventory mapping.

## Cross-Phase Test Matrix Summary

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
| Config category | P6 | — | — | — | — |
| Temp-room softcode (dig/recycle occupant-safe) | — | — | — | — | — (P6 docs) |
