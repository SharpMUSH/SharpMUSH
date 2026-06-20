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

## 8. Engine-extension hooks (Phase 2b) — the C# `@hook`

Your `[SharpPlugin] IPlugin` may also implement any subset of three **engine-extension hooks** (all in
`SharpMUSH.Library.Plugins`). They are the compiled-C# analog of softcode `@hook`: intercept command
execution, and react to connection and object lifecycle. You implement the interface; the host consults it
at the right seam. Hooks **compose with** softcode `@hook` — they never replace it — and a host with no hook
plugins runs exactly as before.

```csharp
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

[SharpPlugin]
public sealed class MyPlugin : PluginBase,
    ICommandInterceptor, IConnectionHook, IObjectLifecycleHook
{
    public override string Id => "myplugin";

    // (a) ICommandInterceptor — runs at the command seam alongside softcode BEFORE/OVERRIDE/AFTER.
    //     BeforeAsync returning false VETOES the command (skips the body, short-circuits dispatch).
    public ValueTask<bool> BeforeAsync(IMUSHCodeParser parser, string command)
        => ValueTask.FromResult(!command.StartsWith("@danger", StringComparison.OrdinalIgnoreCase));

    //     TryOverrideAsync returning non-null SHORT-CIRCUITS the built-in with your result.
    public ValueTask<Option<CallState>?> TryOverrideAsync(IMUSHCodeParser parser, string command)
        => ValueTask.FromResult<Option<CallState>?>(null);

    //     AfterAsync observes the completed command (result discarded).
    public ValueTask AfterAsync(IMUSHCodeParser parser, string command) => ValueTask.CompletedTask;

    // (b) IConnectionHook — wired as an IConnectionService.ListenState listener at boot.
    public ValueTask OnLoginAsync(long handle, DBRef player)      => ValueTask.CompletedTask;
    public ValueTask OnDisconnectAsync(long handle, DBRef? player) => ValueTask.CompletedTask;
    // (OnConnectAsync also available; all default to no-ops.)

    // (c) IObjectLifecycleHook — OnCreatedAsync fires next to the softcode OBJECT`CREATE event;
    //     OnDestroyingAsync fires before the object is destroyed (it is still readable from the DB).
    public ValueTask OnCreatedAsync(DBRef obj, DBRef creator) => ValueTask.CompletedTask;
    public ValueTask OnDestroyingAsync(DBRef obj)            => ValueTask.CompletedTask;
}
```

**Notes**

- **Implement only what you need.** Every callback on all three interfaces has a no-op default, so a plugin
  that only wants `OnCreatedAsync` implements `IObjectLifecycleHook` and overrides that one method.
- **Veto vs. override.** A `BeforeAsync` veto mirrors a softcode `IGNORE` that returns false (the command
  body is skipped, the after seams still run). A non-`null` `TryOverrideAsync` mirrors a softcode `OVERRIDE`
  (your `Option<CallState>` is returned in place of the built-in).
- **Hooks are isolated.** Every hook call is wrapped in `try/catch` by the host (`IPluginHookDispatcher` for
  command/object hooks; the connection-listener wrapper for connection hooks), so a throwing hook is logged
  and never aborts dispatch.
- **Ordering.** Command and object hooks fire in plugin load order; the first interceptor to veto or override
  wins.
- **No unification fuss.** The hook interfaces live in `SharpMUSH.Library`, which your plugin references with
  `<Private>false</Private>` — so they unify with the host automatically across the isolation boundary.

The worked Phase 2b fixture is again `SamplePlugin`: it observes every command (and vetoes `+vetome`) via
`ICommandInterceptor`, and records created objects via `IObjectLifecycleHook`; the integration tests assert
the interceptor fires, the veto skips the command body, and the create hook is invoked.

## 9. Publishing a managed package (Phase 4 — package-manager DLL distribution)

Instead of asking operators to hand-copy your DLL into `plugins/`, you can ship it through the **package
manager** as a `kind: managed` package. Installing it verifies your binaries against SHA-256 hashes you publish
and, once the operator opts in, deposits them into `plugins/<id>/` for the loader to pick up on the next boot.

### Author the `package.yaml`

A managed package is a package directory in a git repo (exactly like a softcode package) whose `package.yaml`
declares `kind: managed` and a `binaries:` block. It carries **no** softcode `objects:` and **no**
`application:` block — only the compiled DLL(s):

```yaml
package: my-plugin            # also the plugins/<id>/ directory name
version: "1.0.0"
authors: [You]
description: "What it does"
kind: managed
binaries:
  min_server_version: ">=1.0"   # the plugin/server contract version your DLL was built against
  files:
    - file: MyPlugin.dll
      sha256: <64-hex SHA-256 of MyPlugin.dll>
    - file: MyPlugin.deps.json   # ship the .deps.json so the loader resolves your private deps
      sha256: <64-hex SHA-256>
    - file: plugin.json          # your ordering metadata (id/version/dependencies/priority)
      sha256: <64-hex SHA-256>
```

`file:` entries are **flat names** (no path separators) — they deposit directly into `plugins/<id>/`. Compute
each hash from the built bytes, e.g. `sha256sum MyPlugin.dll`. Commit `package.yaml` **and** the listed files
together in the package directory; release versions are tagged exactly like softcode packages
(`<package-dir>/v<semver>`), and the installer reads the bytes from that commit, so a moved tag cannot smuggle
different bytes than the hash you signed.

### How an operator installs it

Managed installs are gated more strictly than softcode, because **a managed package runs arbitrary compiled C#
in full server trust — there is no sandbox** (same posture as a hand-dropped plugin). The operator must:

1. Add your package id to the server's `ManagedPackages:AllowList` (or set `ManagedPackages:AllowAll` on a
   single-operator/dev box), and
2. confirm the install with the explicit managed-code opt-in (`allow_managed_code` on the apply).

Then the package manager verifies every file's hash, refuses anything built for a newer `min_server_version`
than the server provides, and deposits the verified bytes into `plugins/<id>/`. **Your plugin loads on the next
server boot** — a freshly-installed managed package is not hot-loaded into the running engine.

Uninstalling removes `plugins/<id>/` and, if your plugin is command/function/hook-only (unloadable, see §7),
unloads it from the live engine first.

> Authoring shape is unchanged from §1–§8 — a managed package is just the distribution wrapper around the same
> `EnableDynamicLoading` plugin DLL. The carried `MyPlugin.deps.json` and `plugin.json` are the same files you
> would otherwise hand-copy.

## Later phases (not yet available)

Live hot-load of a newly-installed managed package (loading it into the running engine without a reboot) is a
possible future nicety. This guide will grow as those seams ship.
