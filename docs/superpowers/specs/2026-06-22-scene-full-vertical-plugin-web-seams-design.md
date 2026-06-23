# Phase 9 — Full Scene Vertical into the Plugin + General Plugin Web-Contribution Seams

**Date:** 2026-06-22
**Status:** Approved (design); implementing.
**Scope:** Plugin framework (new web-contribution seam), Scene plugin, Server, Library, Client (minimal glue), 3 providers.
**Builds on:** Phase 8 (storage already in the plugin behind accessors).

## Goal

Remove ALL remaining scene-specific code from the core "main" assemblies (`SharpMUSH.Library`,
`SharpMUSH.Server`) by giving the plugin framework the ability to **register services AND interact
with the ASP.NET pipeline from within the plugin itself** — so a plugin can contribute controllers,
hubs, endpoints, and services like any ASP.NET module. Scene is the first consumer.

## Principle

A plugin configures the web app across the two ASP.NET phases, both piped through the loader:
1. **ConfigureServices** — already piped via `IServiceRegistrar.RegisterServices(IServiceCollection)`.
   The plugin calls standard ASP.NET config directly, including
   `services.AddControllers().AddApplicationPart(<pluginAssembly>)` (the "FromAssembly" controller load)
   and `services.AddSignalR()`. No new type needed for controllers.
2. **Pipeline/endpoints** — NOT yet piped. Add one general seam.

## New framework seam — `IEndpointContributor`

```csharp
// SharpMUSH.Library/Plugins/IEndpointContributor.cs (host-shared, added to SharedContractTypes)
public interface IEndpointContributor
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
```
- `PluginCatalog` collects plugins implementing it (alongside the other `I*Source` buckets).
- `Program.ConfigureApp` invokes each plugin's `MapEndpoints(app)` after the host's own
  `MapControllers()` / `MapHub<GameHub>()`. A plugin can now `endpoints.MapHub<SceneHub>("/hubs/scene")`,
  map custom routes, etc. Fully general.

## New assembly — `SharpMUSH.Plugins.Scene.Contracts`

Holds the scene types host AND plugin must share (the host's deferred Client + any host glue reference
the contract, the plugin implements/produces it):
- Models: `Scene`, `ScenePose`, `ScenePoseEdit`, `ScenePlot`, `SceneMember` (moved from
  `SharpMUSH.Library/Models/Scene/`).
- `SceneEventMessage` (moved from `SharpMUSH.Library/Models/Portal/`).
- `ISceneService` (moved from `SharpMUSH.Library/Services/Interfaces/`).

Wiring: the assembly is added to the plugin loader's host-shared set (`SharedContractTypes` +/or
`DefaultSharedAssemblyNames`) so host and plugin ALC unify on the same `Type`. `SharpMUSH.Plugins.Scene`,
`SharpMUSH.Server`, and `SharpMUSH.Client` reference it.

## Moves

1. **`SceneController`** (`SharpMUSH.Server/Controllers/`) → `SharpMUSH.Plugins.Scene` (e.g.
   `Web/SceneController.cs`). Registered by the plugin's `RegisterServices` via
   `AddControllers().AddApplicationPart(typeof(ScenePlugin).Assembly)`. Route `api/scenes` unchanged →
   no HTTP/Client impact.
2. **Scene realtime** → a plugin-owned **`SceneHub : Hub<ISceneHubClient>`** in the plugin, with
   `ISceneHubClient.ReceiveSceneMessage(SceneEventMessage)` and `JoinScene`/`LeaveScene` group methods,
   mapped at `/hubs/scene` via `IEndpointContributor`. The plugin's existing
   `IBridgeSubscriptionSource` leg forwards `game.scene.*` to the SceneHub's `scene:{id}` groups via
   `IHubContext<SceneHub, ISceneHubClient>`.
   - Remove from `SharpMUSH.Server/Hubs/GameHub.cs`: `IGameHubClient.ReceiveSceneMessage`,
     `JoinScene`/`LeaveScene`, `SceneGroupName`, `SendToSceneAsync`.
3. **Migration counter seed** → the plugin's `Migration_AddScenes` (Surreal `UPSERT counter:scene_id`
   / `counter:pose_id`; Memgraph counters MERGE-on-first-use so no seed — verify). Remove the seed from
   the core provider migrations.

## Stays in `SharpMUSH.Library` (documented, not scene code)

- `NotificationType.Scene` — a single enum value; an enum cannot be partially extended in the plugin.
- `IConnectionStateService.OnSceneEventReceived` — a Client-realtime interface member; it references
  `Contracts.SceneEventMessage`. (Lives with the deferred Client surface.)

## Minimal Client glue (the only Client change; rest of Client stays deferred)

The Client must point its scene realtime at the new `/hubs/scene` instead of GameHub's scene methods,
or it breaks at runtime. Surgically update only the scene-realtime wiring: the SceneHub connection
(JoinScene / OnSceneEventReceived), the `IGameHubConnection` scene method → a scene-hub connection, and
`SceneService`/`ConnectionStateService` reference `Contracts`. No other Client work.

## Testing

- Scene suite (storage/commands/functions) green on all 3 providers (unchanged from Phase 8).
- `SceneController` endpoints serve from the plugin (ApplicationPart discovered): `GET /api/scenes`,
  `/api/scenes/{id}`, `/poses`, `/members` return as before.
- `SceneHub` realtime: a scene event published to `game.scene.{id}` reaches a `/hubs/scene` group
  subscriber via the bridge; `JoinScene`/`LeaveScene` group management works.
- `IEndpointContributor` seam: a test plugin's `MapEndpoints` is invoked and its route responds.
- **Boundary:** `SharpMUSH.Server` contains no scene controller/hub/types; `SharpMUSH.Library` contains
  no scene models/`ISceneService`/`SceneEventMessage`. Removing the plugin leaves the host with no
  scene REST/realtime endpoints (they simply aren't mapped).
- ALC: the plugin's controller + hub instantiate via host DI and the shared `Contracts` types unify
  (smoke test, like Phase 8's).
- bUnit/Client: the minimal scene-realtime rewire compiles and the scene pages still bind.

## Risks

- **MVC ApplicationPart from a collectible ALC** — controller discovery + activation across the plugin
  ALC; verify routing and DI activation, and that unload still works (Phase 8's loader tests stay green).
- **SignalR hub type in the plugin assembly** — `MapHub<SceneHub>` resolves the hub from DI; ensure the
  hub + `IHubContext<SceneHub>` resolve across the ALC and `Contracts` types serialize over the wire.
- **`Contracts` shared-type identity** — same mechanism proven in Phase 8 (shared assembly by name).
- **Client coupling** — keep the Client change strictly to the scene-realtime hub wiring; do not expand.
