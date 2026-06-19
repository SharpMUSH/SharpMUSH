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

## Later phases (not yet available)

Service registration (`IServiceRegistrar`), migrations (`IMigrationSource`), engine flags (`IFlagSource`),
NATS bridge subscriptions, named extension hooks, hot-reload, and signed package distribution are planned for
later phases. This guide will grow as those seams ship.
