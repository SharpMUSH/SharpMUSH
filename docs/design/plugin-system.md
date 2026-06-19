# Plugin System (C# DLL plugins)

SharpMUSH supports loading **compiled C# plugins** from a `plugins/` directory at boot. A plugin is an
ordinary .NET assembly that contributes `[SharpCommand]`/`[SharpFunction]` definitions (and, in later
phases, services, migrations, flags, bridge subscriptions, and extension hooks) into the live engine —
without recompiling the server.

This document describes the **Phase 1** architecture (command/function contributions) and the **Phase 2a**
contribution seams (DI services, DB migrations, engine flags, NATS bridge subscriptions), and notes the
seams reserved for later phases.

## Why a plugin loader

Engine commands and functions are discovered at **compile time** by source generators that scan
`SharpMUSH.Implementation` for `[SharpCommand]`/`[SharpFunction]` and emit
`SharpMUSH.Implementation.Generated.CommandLibrary.Commands` /
`…FunctionLibrary.Functions`. An external DLL cannot contribute through that path. The plugin loader adds a
**runtime** discovery + registration path so a third-party DLL can extend the engine.

## Load model: McMaster.NETCore.Plugins + shared types

Loading is built on [`McMaster.NETCore.Plugins`](https://github.com/natemcmaster/DotNetCorePlugins). Each
plugin is loaded through its own `PluginLoader`:

```csharp
var loader = PluginLoader.CreateFromAssemblyFile(
    dllPath,
    isUnloadable: false,            // stays false until the Phase 3 hot-reload work
    sharedTypes: SharedContractTypes);
var assembly = loader.LoadDefaultAssembly();
```

**`sharedTypes`** is the host-declared set of contract types that must **unify** across the isolation
boundary, so a `CommandDefinition` produced inside the plugin is the *same* type the host's library expects.
Listing a type here makes the host's copy authoritative regardless of plugin csproj hygiene — it is the real
safety net, not the plugin's `<Private>false>` reference. The set
(`PluginLoaderService.SharedContractTypes`):

`IPlugin`, `SharpPluginAttribute`, `ICommandSource`, `IFunctionSource`, `CommandDefinition`,
`FunctionDefinition`, `SharpCommandAttribute`, `SharpFunctionAttribute`, `IMUSHCodeParser`, `CallState`,
`PluginManifest`, `PluginBase`, `Option<CallState>` — plus the Phase 2a contribution surfaces
`IServiceRegistrar`, `IFlagSource`, `IMigrationSource`, `IBridgeSubscriptionSource`, and `PluginFlag` (so a
plugin's loaded instance pattern-matches against the host's interface types across the isolation boundary).

> **Host dependency closure.** Because McMaster eagerly walks the shared `SharpMUSH.Library` reference
> closure and resolves each referenced assembly against the host's *default* load context, the host must
> carry its full dependency closure beside its binary. In particular `FSharp.Core` (a runtime transitive of
> `SharpMUSH.Library` via `TelnetNegotiationCore` that the engine never JITs and that is normally trimmed
> from output) is materialized into the `SharpMUSH.Server` output by an explicit `FSharp.Core`
> `PackageReference`. A host that loads plugins must not be trimmed below its real closure.

## Contract surface (`SharpMUSH.Library`)

The contracts live in `SharpMUSH.Library` (which plugins already reference) so plugin and host share one
definition:

- **`IPlugin`** — identity + lifecycle: `Id`, `Version`, `Dependencies` (other plugin ids → load order),
  `Priority` (tie-break), `Initialize(IServiceProvider)` (one-time setup; default no-op).
- **`[SharpPlugin]`** — marks the single entry type per assembly so discovery needs no blind scan.
- **`ICommandSource`** → `IEnumerable<CommandDefinition> GetCommands()` and
  **`IFunctionSource`** → `IEnumerable<FunctionDefinition> GetFunctions()` — the contribution surfaces.
- **`PluginManifest`** — `Id`, `Version`, `Dependencies`, `Priority`, `MinServerVersion`; loaded from a
  `plugin.json` next to the DLL (`System.Text.Json`). Drives load order and compatibility.
- **`PluginBase : IPlugin, ICommandSource, IFunctionSource`** — convenience base. Its default
  `GetCommands`/`GetFunctions` use reflection to read the generator-produced
  `SharpMUSH.Implementation.Generated.CommandLibrary.Commands` /
  `…FunctionLibrary.Functions` static fields **in the plugin's own assembly**. Because those values' element
  type (`CommandDefinition`/`FunctionDefinition`) is a shared type, they cast cleanly to the host's type.
  So a plugin author writes nothing but ordinary `[SharpCommand]`/`[SharpFunction]` methods plus one
  `[SharpPlugin] : PluginBase` overriding `Id`.

### The generator-as-analyzer (load-bearing)

A plugin references `SharpMUSH.Implementation.Generated` **as an analyzer**
(`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`). The generator's emitted class name and
namespace are hard-coded (`SharpMUSH.Implementation.Generated.CommandLibrary`/`FunctionLibrary`), so the
plugin assembly gets **its own** populated copy of those dictionaries — exactly the same authoring
experience as in-tree code. (Verified: a built `SamplePlugin.dll` exposes
`Generated.CommandLibrary.Commands` containing its `+PING`, and `Generated.FunctionLibrary.Functions`
containing its `pluginadd`.)

## Registration policy: `IsSystem = true`

Plugin commands/functions are **compiled C#**, the same tier as built-ins, so they register with
**`IsSystem = true`** (the only `IsSystem = false` tier is softcode `@function`). This matters:

- `MUSHCodeParser.BuildCommandTrie` adds **only `IsSystem = true`** entries to the command trie. Registering
  with `IsSystem = true` means plugin commands enter the trie automatically and support **abbreviation**
  (prefix matching) like built-ins — **no parser change is required**. The trie is rebuilt for each parse
  (each `FromState`), so entries added at boot are visible on every subsequent command.
- Nothing in the engine treats engine-vs-plugin differently on `IsSystem`. The only `IsSystem` consumers are
  function-precedence (`CallFunction`), `builtin`/`local` introspection (`@command`, `functions()`), and
  semantic-token coloring — all of which correctly classify a plugin as a built-in-tier contribution.

Collisions with an existing entry (the engine loads first) are **logged and skipped** (`TryAdd`); the
existing definition wins.

## Discovery → ordering → load (single pass)

`SharpMUSH.Implementation.Services.PluginLoaderService` is the **one** place a DLL is loaded:

1. **Discovery.** Scan `AppContext.BaseDirectory/plugins` for `*.dll` at the top level and one level down
   (`plugins/<id>/*.dll`). Read a sibling `plugin.json` for ordering metadata; fall back to file-name
   defaults when absent.
2. **Order resolution.** Topological sort by `Dependencies` so every plugin loads after all of its declared
   dependencies. Ties (no dependency relationship) break by `Priority` then `Id`. Dependency cycles are
   detected, logged, and the cyclic plugins are skipped (the rest still load). Missing declared dependencies
   are logged and the dependent still loads.
3. **Load.** For each candidate in order: create its `PluginLoader`, `LoadDefaultAssembly()`, find the single
   `[SharpPlugin] IPlugin` type, `Activator.CreateInstance`. Every plugin is wrapped in `try/catch` so **one
   bad DLL never aborts boot**. The loader returns the instantiated plugins; it does **not** initialize them
   or apply any contribution — that is the catalog's / manager's job.

## Two-phase boot — load plugins **once** (Phase 2a)

Phase 1 registered commands/functions from an `IHostedService` (`PluginBootstrapService`) that runs
**after** the container is built. But DI registration, `db.Migrate()` (run inside the `ISharpDatabase`
singleton factory in `Startup`), and flag seeding all happen **during** container construction — before any
post-build hosted service can run. So plugins must be discovered in a **pre-build** pass.

`SharpMUSH.Implementation.Services.PluginCatalog` solves this by loading **once**:

- **`PluginCatalog.Build(services, logger)`** is called once from `Startup.ConfigureServices`, **before** the
  engine's services and the `ISharpDatabase` registration. It runs `PluginLoaderService.LoadAll` (the single
  DLL-load pass), then for each loaded plugin: applies any `IServiceRegistrar.RegisterServices` straight into
  the `IServiceCollection` (pre-build DI), and classifies the plugin into the migration/flag/bridge
  contribution buckets it exposes (`MigrationSources`, `FlagSources`/`AllFlags`, `BridgeSources`).
- The catalog is registered as a **singleton**. The DB factory reads `MigrationSources` + `AllFlags`,
  `NatsBridgeService` reads `BridgeSources`, and the post-build `PluginManager` reads `Plugins` — none of
  them re-loads a DLL.
- **`PluginManager`** (still driven by `PluginBootstrapService`, an `IHostedService`) no longer loads
  anything: it iterates `catalog.Plugins`, calls `Initialize(rootProvider)`, and registers each plugin's
  `ICommandSource.GetCommands()` / `IFunctionSource.GetFunctions()` with `IsSystem = true`. This is still
  post-build because command/function registration needs the live libraries and the root provider, and is
  ordered **before** the softcode package / `@STARTUP` bootstrap stages so plugin commands/functions are
  present when those run.

## The four Phase 2a contribution seams

A plugin's `[SharpPlugin] IPlugin` may implement any subset of these (all live in `SharpMUSH.Library.Plugins`):

| Interface | Where it is applied | Wiring |
|-----------|---------------------|--------|
| `IServiceRegistrar` | **Pre-build**, into the host `IServiceCollection` | `PluginCatalog.Build` calls `RegisterServices(services)` in `Startup.ConfigureServices`, before `AddMediator()`. |
| `IFlagSource` (→ `PluginFlag` records) | **DB migration**, seeded alongside built-in flags | The catalog's `AllFlags` is passed into each DB constructor. Arango UPSERTs them after `UpgradeAsync`; Memgraph/Surreal MERGE/UPSERT them after the built-in flag batch. Idempotent (keyed on flag name). |
| `IMigrationSource` (provider-tagged) | **DB migration**, after the built-in batch | The catalog's `MigrationSources` is passed into each DB constructor. Arango feeds `ArangoMigrationAssembly` to `migrator.AddMigrations`; Memgraph runs `CypherStatements`; Surreal runs `SurrealStatements`. Each statement is isolated. |
| `IBridgeSubscriptionSource` | **NATS→SignalR bridge** background loop | `NatsBridgeService` runs each `BridgeSources` entry's `RunAsync(nats, hubContext, ct)` alongside its built-in output/room/scene subscriptions, each wrapped in `try/catch` so one faulting subscription cannot tear down the loop. |

`IMigrationSource` and `IBridgeSubscriptionSource` keep their parameter types loose (`Assembly?` / `object`)
so the contracts live in `SharpMUSH.Library` without forcing a SignalR dependency there; the host passes the
concrete `NatsConnection` and `IHubContext<GameHub, IGameHubClient>` for the plugin to cast.

### How the DB factory receives the migration/flag sources

Each provider's primary constructor gained two **optional** trailing parameters —
`IReadOnlyList<IMigrationSource>? migrationSources = null` and `IReadOnlyList<PluginFlag>? pluginFlags = null`
(null-coalesced to empty). The `Startup` `ISharpDatabase` factory threads `catalog.MigrationSources` and
`catalog.AllFlags` into them. Staging databases (created from a live DB) pass nothing, so plugin
migrations/flags are not re-seeded into a derived staging copy.

### Resolving services from a plugin command

Plugin commands/functions resolve engine services **at call time** from the parser, the supported and
unload-friendly pattern:

```csharp
var notify = parser.ServiceProvider.GetRequiredService<INotifyService>();
```

`IMUSHCodeParser.ServiceProvider` is part of the interface; there is no static-service-injection mechanism.

## Reserved seams (later phases)

The architecture leaves these seams for the committed later phases:

- **Phase 2a** — *implemented* — contribution interfaces (`IServiceRegistrar` pre-build DI, `IMigrationSource`,
  `IFlagSource`, `IBridgeSubscriptionSource`) via the two-phase `PluginCatalog` boot (see above).
- **Phase 2b** — named **extension-point hooks** (command pre/post/override, connection/object/startup
  lifecycle).
- **Phase 3** — hot-reload/unload (`isUnloadable: true`, reload + `WeakReference`-dead gate).
- **Phase 4** — package-manager DLL distribution (signed/hashed manifest, trust gate).
- **Phase 5** — extract the Scene system as the reference plugin via the Phase-2 seams.

## See also

- `docs/guides/writing-a-plugin.md` — the worked author guide (the `SamplePlugin` fixture).
