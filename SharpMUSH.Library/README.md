# SharpMUSH.Library — Plugin Contract Package

This package is the **contract surface external SharpMUSH plugins build against**. A plugin in a
separate repository adds a `<PackageReference Include="SharpMUSH.Library" />`, writes a
`[SharpPlugin] sealed class : PluginBase` entry type, decorates static methods with `[SharpCommand]` /
`[SharpFunction]`, and ships the resulting DLL (plus a `plugin.json`) to a SharpMUSH server's plugin
directory.

> **Interim packaging note.** Today the whole `SharpMUSH.Library` assembly is shipped as the contract
> package rather than a slimmer `SharpMUSH.Plugin.Abstractions`. `CommandDefinition` /
> `FunctionDefinition` (required by `PluginBase` / `ICommandSource` / `IFunctionSource`) reference
> `IMUSHCodeParser`, which transitively pulls in most of `SharpMUSH.Library` (Services, Models,
> DiscriminatedUnions). Carving those out would relocate the bulk of the Library into the "abstractions"
> assembly and split the dynamic loader's shared-type set across two assemblies. Shipping the Library
> whole keeps a single shared contract assembly, which is exactly what the loader's
> `PluginLoaderService.SharedContractTypes` requires. See the `TODO slim abstractions` note in
> `SharpMUSH.Library.csproj`.

## What's in the contract surface

Identity & lifecycle
- `IPlugin`, `PluginBase`, `[SharpPlugin]` (`SharpMUSH.Library.Attributes.SharpPluginAttribute`)
- `PluginManifest`, `PluginContractVersion`

Command / function contribution
- `[SharpCommand]`, `[SharpFunction]`
- `ICommandSource` / `IFunctionSource`, `CommandDefinition` / `FunctionDefinition`
- `IMUSHCodeParser`, `CallState`, `Option<…>` (the types command/function bodies use)

Phase 2a contribution surfaces
- `IServiceRegistrar`, `IMigrationSource`, `IFlagSource`, `IBridgeSubscriptionSource`, `PluginFlag`

Phase 2b engine-extension hooks
- `ICommandInterceptor`, `IConnectionHook`, `IObjectLifecycleHook`

## Contract-version alignment

The package `Version` is aligned to `PluginContractVersion.Current` (currently **1.0.0**). A managed
package's `binaries.min_server_version` is checked against this value at load time, so a plugin built
against a newer contract is refused by an older server rather than failing obscurely. **When the plugin
contract surface changes, bump both `PluginContractVersion.Current` and the package `<Version>`
together.**

## How an external plugin references it

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Emits a .deps.json and marks this as a dynamically-loadable plugin. -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <!-- The contract surface. ExcludeAssets=runtime keeps the host's copy authoritative at load time;
         the loader's SharedContractTypes unifies the types across the isolation boundary. -->
    <PackageReference Include="SharpMUSH.Library" Version="1.0.0">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>

    <!-- The source generator, referenced as an analyzer. Your plugin's [SharpCommand]/[SharpFunction]
         methods produce SharpMUSH.Implementation.Generated.CommandLibrary.Commands /
         FunctionLibrary.Functions inside YOUR assembly, which PluginBase reflects at load time. -->
    <PackageReference Include="SharpMUSH.Implementation.Generated" Version="1.0.0"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

```csharp
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

[SharpPlugin]
public sealed class MyPlugin : PluginBase
{
  public override string Id => "com.example.my-plugin";
  public override string Version => "1.0.0";
}

public static class MyCommands
{
  [SharpCommand(Name = "@HELLO")]
  public static ValueTask<Option<CallState>> Hello(IMUSHCodeParser parser, SharpCommandAttribute _2)
    => /* ... */;
}
```

Ship the built plugin DLL together with a `plugin.json` (id / version / dependencies / priority /
min server version) into the server's plugin directory. See `docs/guides/writing-a-plugin.md` in the
SharpMUSH repository for the full walkthrough.
