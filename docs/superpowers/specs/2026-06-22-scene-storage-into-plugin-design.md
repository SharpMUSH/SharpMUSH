# Phase 8 — Scene Storage Relocates into the Plugin (Connection-Accessor Seam)

**Date:** 2026-06-22
**Status:** Approved (design); spec pending user review
**Scope:** Plugin framework + Scene plugin + 3 DB providers. Moves `ISceneService` storage out of core.
**Prerequisite:** the SurrealDB ancestor-overhead perf fix must land first so this lands on a green branch.

## Problem

The Scene plugin (`SharpMUSH.Plugins.Scene`) owns commands, functions, `Migration_AddScenes`
(`IMigrationSource`), `SCENE_ROOM` (`IFlagSource`), and the `game.scene.*` bridge — but **not its
persistence**. `ISceneService` is implemented as `partial class <Provider>` in the core DB projects
(`ArangoDatabase.Scene.cs`, `MemgraphDatabase.Scene.cs`, `SurrealDatabase.Scene.cs`), sharing each
provider's private connection/helpers. Remove the plugin DLL and the three core providers still carry
scene collections, edge definitions, and storage code. Not a clean plugin seam. (See memory
`scene-storage-not-in-plugin`.)

## Goal

Move the three storage implementations **into the plugin**, behind a per-provider **connection-accessor
seam** exposed by core, and register `ISceneService` from the plugin with an **ASP.NET-style, config-
driven, behavior-extensible** registration. Removing the plugin must leave core with **no** scene
storage. Provider-native queries (AQL/Cypher/SurrealQL) move **verbatim** — lowest behavioral risk.

## Components

### 1. Connection-accessor interfaces (in `SharpMUSH.Library`, host-shared)
One per provider, exposing exactly what the relocated storage consumes (mapped from the current
partials):
- `IArangoStorageAccessor` — the `IArangoContext` (and the `.Query`/`.Document` entry points the
  scene code uses).
- `IMemgraphStorageAccessor` — the Neo4j `IDriver`/session runner.
- `ISurrealStorageAccessor` — `ExecuteAsync(...)`, the `Rid(...)` helper, and `Select`.

Each provider class implements its own accessor (surfacing what is private today) and registers it in
DI as it already constructs its connection. Interfaces live in `SharpMUSH.Library` so both host and
plugin share the **same** `Type` across the plugin `AssemblyLoadContext`.

### 2. Relocate the storage into `SharpMUSH.Plugins.Scene/Storage/`
Move the three `*.Scene.cs` files into the plugin as standalone classes
(`ArangoSceneStorage` / `MemgraphSceneStorage` / `SurrealSceneStorage`) implementing `ISceneService`,
each taking its accessor via constructor injection. **Queries copied verbatim.** Move the scene name
constants out of core `DatabaseConstants` into a plugin-owned constants type alongside (the migration —
already in the plugin via `IMigrationSource` — references them from there too).

Remove `: ISceneService` from the three provider classes and the core `Startup.cs` `ISceneService`
tri-cast registration.

### 3. ASP.NET-style registration (config + behavior layering)
`ScenePlugin.RegisterServices(services)` (the existing `IServiceRegistrar` seam) calls a new extension:

```csharp
services.AddSceneSystem(configuration)
        .AddBehavior<SceneAuditBehavior>()
        .AddBehavior<SceneCachingBehavior>();
```

- `AddSceneSystem(IConfiguration)` registers each provider's storage as a **keyed** `ISceneStorage`
  (`"arangodb"`/`"memgraph"`/`"surrealdb"` — keyed DI is already used in this repo on net10.0), reads
  the active provider from config, and registers `ISceneService` via a **factory** that resolves the
  active keyed storage and wraps it with the registered behaviors **in order** (the
  `IHttpClientBuilder.AddHttpMessageHandler` model). Returns an `ISceneSystemBuilder` for chaining.
- `ISceneServiceBehavior` is a delegating decorator over `ISceneService`. `.AddBehavior<T>()` layers
  new behavior (caching, audit, NATS broadcast, permission gates) **without touching the core or the
  storage**. Net chain: `behaviorN → … → behavior1 → storage-backed core`.
- Decoration is **hand-rolled** in the factory (no Scrutor) to avoid adding a dependency into the
  plugin ALC. (Scrutor `Decorate<>` is the drop-in alternative if preferred later.)

### 4. `ISceneService` stays in `SharpMUSH.Library`
The shared contract stays put (host + plugin reference it). The WASM client still registers no impl.

## Key risk — AssemblyLoadContext type identity

The plugin must reference the three DB-client packages (ArangoDBNetStandard, Neo4j.Driver,
SurrealDb.Net) to use the accessor-returned connections. Under McMaster plugin loading, those client
types **must be shared from the host ALC**, not reloaded into the plugin ALC, or an accessor-returned
client cannot be cast/used inside the plugin (type-identity mismatch). The plugin loader config must
mark these client assemblies (and the `SharpMUSH.Library` accessor interfaces) as host-shared. This is
the make-or-break detail; prototype it first.

Secondary risk: the accessor must expose the connection/helpers **without** re-leaking scene concepts
back into core (keep the accessor generic — "give me the connection + the primitive helpers," nothing
scene-specific).

## Testing

- The existing scene suite (≈29–30 ×3 providers: pose_next ordering, edit versioning/undo-redo,
  soft-delete, member isCurrent, scenewhere, object-edge + Name snapshot round-trip, 1-based ids) must
  pass **unchanged** with storage now plugin-owned, on all three providers.
- New: with the Scene plugin **not loaded**, core providers expose **no** `ISceneService` and no scene
  collections/labels/tables are referenced by core code (boundary proof — the whole point of Phase 8).
- New: `AddSceneSystem` selects the storage matching the configured provider (keyed resolution); an
  `AddBehavior<T>()` decorator is invoked in order around a service call (proves the layering seam).
- ALC: a smoke test that the loaded plugin can obtain and use each provider's connection accessor
  (catches the type-identity risk early).

## Non-goals

- No change to scene *behavior*, schema, or the softcode capture/`@message`/FORMAT work.
- No generic cross-provider graph abstraction (explicitly rejected in favor of verbatim relocation).
- ISceneService method surface is unchanged.
