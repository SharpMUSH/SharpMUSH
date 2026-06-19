# Writing a SharpMUSH plugin

A SharpMUSH plugin is a compiled .NET assembly that adds commands and functions to the engine at runtime —
no server recompile. Authoring is identical to writing in-tree engine code: you write ordinary
`[SharpCommand]`/`[SharpFunction]` methods. This guide walks the worked example used by the test suite,
`SamplePlugin` (`SharpMUSH.Tests/Fixtures/SamplePlugin/`).

See `docs/design/plugin-system.md` for the architecture and the registration/load-order semantics.

## 1. The project file

A plugin targets `net10.0`, enables dynamic loading, references `SharpMUSH.Library` as a non-private
contract reference, and references the source generator **as an analyzer**:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Emits a .deps.json and keeps the contract assembly out of the plugin's private bin. -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <!-- The contract surface. Private=false + ExcludeAssets=runtime keep SharpMUSH.Library out of the
         plugin output so the host's copy loads. (The host's sharedTypes is the real safety net either way.) -->
    <ProjectReference Include="path/to/SharpMUSH.Library/SharpMUSH.Library.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <!-- The source generator as an analyzer: your [SharpCommand]/[SharpFunction] methods produce
         SharpMUSH.Implementation.Generated.CommandLibrary.Commands / FunctionLibrary.Functions in THIS
         assembly. ReferenceOutputAssembly=false: run the generator, do not link the generator project. -->
    <ProjectReference Include="path/to/SharpMUSH.Implementation.Generated/SharpMUSH.Implementation.Generated.csproj"
        OutputItemType="Analyzer"
        ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

## 2. The entry type

Exactly one type per assembly is marked `[SharpPlugin]` and implements `IPlugin`. Derive from `PluginBase`
to inherit the default `GetCommands`/`GetFunctions` (which reflect the generator output in your assembly) and
override just the identity:

```csharp
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Plugins;

[SharpPlugin]
public sealed class SamplePlugin : PluginBase
{
    public override string Id => "sample";
    public override string Version => "1.0.0";
    // Optional: override Dependencies (other plugin ids) and Priority to influence load order.
}
```

## 3. Commands and functions

Write them exactly as in-tree engine code. Resolve any engine services **at call time** from
`parser.ServiceProvider` — that is the supported pattern:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

public sealed class SamplePlugin   // (same class; methods can live anywhere in the assembly)
{
    [SharpCommand(Name = "+PING", MinArgs = 0, MaxArgs = 1)]
    public static async ValueTask<Option<CallState>> Ping(IMUSHCodeParser parser, SharpCommandAttribute _2)
    {
        var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
        var notify   = parser.ServiceProvider.GetRequiredService<INotifyService>();

        var executor = await parser.CurrentState.KnownExecutorObject(mediator);
        await notify.Notify(executor, "Pong from the sample plugin!", executor);

        return new CallState("Pong from the sample plugin!");
    }

    [SharpFunction(Name = "pluginadd", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
    public static ValueTask<CallState> PluginAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
    {
        var args = parser.CurrentState.Arguments;
        var a = decimal.Parse(args["0"].Message!.ToPlainText());
        var b = decimal.Parse(args["1"].Message!.ToPlainText());
        return ValueTask.FromResult(new CallState((a + b).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
```

Because plugin commands register with `IsSystem = true`, `+PING` lands in the command trie and supports
abbreviation (`+pi`) like any built-in.

## 4. The manifest

Place a `plugin.json` next to the built DLL. It drives discovery order and compatibility:

```json
{
  "id": "sample",
  "version": "1.0.0",
  "dependencies": [],
  "priority": 0,
  "minServerVersion": null
}
```

- `dependencies` — ids of plugins that must load **before** this one (topologically ordered; cycles are
  detected and skipped).
- `priority` — tie-break among plugins with no dependency relationship (lower loads first).

## 5. Build and deploy

```bash
dotnet build YourPlugin.csproj
```

Drop the built `YourPlugin.dll` and its `plugin.json` into the server's `plugins/` directory — either at the
top level (`plugins/YourPlugin.dll`) or in a per-plugin subfolder (`plugins/your-id/YourPlugin.dll`, with the
`plugin.json` beside it). On boot, `PluginBootstrapService` discovers, orders, loads, and registers it. A
failing plugin is logged and skipped; it never aborts server boot.

> The server host must carry its full dependency closure beside its binary for the plugin loader to resolve
> shared types (this is why `SharpMUSH.Server` references `FSharp.Core` explicitly — a transitive the engine
> never JITs but the loader's reference walk requires). If you self-host the engine to load plugins, do the
> same.

## 6. Collisions and load order

- A plugin command/function whose name collides with an existing engine (or earlier-loaded plugin) entry is
  **logged and skipped** — the existing definition wins.
- To guarantee your plugin loads after another, list that plugin's `id` in your `dependencies`.

## 7. Contributing beyond commands/functions (Phase 2a)

Your `[SharpPlugin] IPlugin` may also implement any subset of four **contribution interfaces** (all in
`SharpMUSH.Library.Plugins`). These extend the host beyond commands/functions — DI services, DB migrations,
engine flags, and NATS bridge subscriptions. The host discovers them through the same single load pass: a
pre-build `PluginCatalog` loads your DLL once, applies your `IServiceRegistrar` straight into the container,
and stashes your migration/flag/bridge contributions for the DB factory and bridge service to read. You do
not register anything yourself — implement the interface and the host wires it.

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Plugins;

[SharpPlugin]
public sealed class MyPlugin : PluginBase,
    IServiceRegistrar, IFlagSource, IMigrationSource, IBridgeSubscriptionSource
{
    public override string Id => "myplugin";

    // (a) DI: register your own services into the host container. Applied PRE-BUILD, so your service is
    //     available to the engine the moment the container is built. Resolve engine services in commands
    //     at call time from parser.ServiceProvider as usual.
    public void RegisterServices(IServiceCollection services)
        => services.AddSingleton<MyPluginService>();

    // (b) Flags: seeded into whichever DB backend is active, alongside the built-in flags, during migration.
    //     Idempotent (keyed on Name), so it is safe across re-migration. Mirrors the built-in flag shape.
    public IEnumerable<PluginFlag> Flags =>
    [
        new PluginFlag(
            Name: "MYFLAG", Symbol: "y", Aliases: [],
            SetPermissions: ["wizard"], UnsetPermissions: ["wizard"],
            TypeRestrictions: ["ROOM", "PLAYER", "EXIT", "THING"])
    ];

    // (c) Migrations: provider-tagged. Implement only the backends you support; every member has an
    //     empty/no-op default. These run AFTER the engine's own migration batch.
    public Assembly? ArangoMigrationAssembly => typeof(MyPlugin).Assembly;        // IArangoMigration types here
    public IEnumerable<string> CypherStatements => ["CREATE INDEX ON :MyThing(id)"];   // Memgraph
    public IEnumerable<string> SurrealStatements => ["DEFINE TABLE my_thing SCHEMALESS"]; // SurrealDB

    // (d) NATS bridge: subscribe to your own subjects and forward to SignalR groups, mirroring the engine's
    //     built-in output/room/scene subscriptions. The host runs this alongside the built-ins, isolated in
    //     try/catch. The params are loose (object) so the contract avoids a SignalR dependency — cast them:
    public async Task RunAsync(object natsConnection, object hubContext, CancellationToken ct)
    {
        var nats = (NATS.Client.Core.NatsConnection)natsConnection;
        var hub  = (Microsoft.AspNetCore.SignalR.IHubContext<
                        SharpMUSH.Server.Hubs.GameHub, SharpMUSH.Server.Hubs.IGameHubClient>)hubContext;
        await foreach (var msg in nats.SubscribeAsync<MyMessage>("game.myplugin.*", cancellationToken: ct))
        {
            // forward to a SignalR group...
        }
    }
}
```

**Notes**

- **Implement only what you need.** A plugin that just contributes one DI service implements only
  `IServiceRegistrar`. The catalog classifies each plugin by the interfaces it implements.
- **Flags ride the migration plumbing.** A contributed flag is seeded during `db.Migrate()` on every backend,
  so it is queryable (e.g. `GetObjectFlagQuery`) immediately after boot.
- **Migrations are per-provider.** If your plugin only supports ArangoDB, leave `CypherStatements` /
  `SurrealStatements` at their empty defaults; the other backends simply seed nothing from your plugin.
- **Bridge subscriptions are long-lived.** `RunAsync` should loop until `ct` is cancelled, exactly like the
  built-in subscriptions. A faulting subscription is logged and isolated; it does not tear down the others.

The worked Phase 2a fixture is the same `SamplePlugin` — it registers `SamplePluginService`
(`IServiceRegistrar`), seeds the `SAMPLE_PLUGIN` flag (`IFlagSource`), and runs a bridge subscription
(`IBridgeSubscriptionSource`); the integration tests assert each end-to-end.

## Later phases (not yet available)

Named extension hooks (command pre/post/override, connection/object/startup lifecycle), hot-reload, and
signed package distribution are planned for later phases. This guide will grow as those seams ship.
