# PLUGIN_ID — a SharpMUSH C# DLL plugin

A starter **compiled C# plugin** for [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH).
A plugin is an ordinary `net11.0` assembly that contributes `[SharpCommand]` /
`[SharpFunction]` definitions (and optionally DI services, DB migrations, flags,
NATS-bridge legs, and extension hooks) into the live engine — **no server
recompile**. Authoring is identical to in-tree engine code.

See the [plugin system design](https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/design/plugin-system.md),
the [authoring guide](https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/guides/writing-a-plugin.md),
and the [extensibility overview](https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/design/extensibility-overview.md).

## What's here

```
PLUGIN_ID/
├── PLUGIN_NAME.csproj           # net11.0, EnableDynamicLoading, abstractions + generator refs
├── Plugin.cs                    # [SharpPlugin] : PluginBase with a sample command + function
├── plugin.json                  # loader metadata (id/name/description/entry/version/dependencies/priority)
├── package.yaml                 # kind: plugin — distributes the DLL via the package manager
├── README.md
├── LICENSE
└── .github/workflows/
    └── build-and-release.yml     # build + test + SHA-256 + rewrite hashes + release (deterministic)
```

## Build prerequisites — the parallel NuGets

This template references two SharpMUSH contract packages (versioned **1.1.0**,
aligned to the server's `PluginContractVersion`):

- **`SharpMUSH.Library`** — the contract surface (`IPlugin`, `PluginBase`, the
  `[SharpPlugin]`/`[SharpCommand]`/`[SharpFunction]` attributes, `IMUSHCodeParser`,
  `CallState`, the contribution + hook interfaces). (A slim
  `SharpMUSH.Plugin.Abstractions` split is a future refinement.)
- **`SharpMUSH.Implementation.Generated`** — the source generator, referenced **as
  an analyzer**, that emits your `Generated.CommandLibrary`/`FunctionLibrary` so
  `PluginBase` can reflect your commands/functions in this assembly.

These packages are produced by the SharpMUSH repo's `publish-plugin-nugets.yml`
workflow. **Until they are published to a public feed**, build against an in-repo
SharpMUSH checkout: comment out the
two `PackageReference` lines in the csproj and uncomment the `ProjectReference`
block right below them (fix the relative paths). That reproduces the in-tree plugin
shape exactly — see `SharpMUSH.Plugins.Scene` and
`SharpMUSH.Tests/Fixtures/SamplePlugin`. The template's C# is syntactically valid,
but it **will not build standalone** until one of those reference paths resolves.

```bash
dotnet build PLUGIN_NAME.csproj -c Release
```

## The plugin in one screen

`Plugin.cs` is a single `[SharpPlugin] : PluginBase` class:

- `+PLUGINCMDTOKEN` — a command. Plugin commands register `IsSystem = true` (same
  tier as built-ins), so they enter the command trie and support abbreviation.
- `PLUGINFNTOKEN(a,b)` — a function.
- Engine services are resolved **at call time** from `parser.ServiceProvider` — the
  supported, unload-friendly pattern. Never cache a resolved service in a field.

To contribute beyond commands/functions, implement any subset of the contribution
and hook interfaces from `SharpMUSH.Library.Plugins` (commented at the bottom of
`Plugin.cs`). Note: command/function/hook-only plugins can be **hot-unloaded**;
adding a DI service, migration, flag, or bridge subscription makes the plugin
load-once.

## Deploying by hand

Drop the built `PLUGIN_NAME.dll`, `PLUGIN_NAME.deps.json`, and `plugin.json` into
`PLUGIN_ID/` under the server's installed-plugins folder (`<world path>.plugins/`
on the data volume, e.g. `/app/data/lightning.plugins`, or `SHARPMUSH_PLUGINS_PATH`).
On boot the loader discovers, orders,
loads, and registers it. A failing plugin is logged and skipped — it never aborts
boot.

> The server host must carry its full dependency closure beside its binary for the
> loader to resolve shared types (this is why `SharpMUSH.Server` references
> `FSharp.Core` explicitly). If you self-host the engine to load plugins, do the same.

## Distributing as a plugin package

`package.yaml` (`kind: plugin`) distributes this DLL through the package manager
instead of a hand-copy. It must list `plugin.json` in `binaries.files`, and
`plugin.json`'s `id` must equal the package id (`PLUGIN_ID`). `entry` names the
plugin's own DLL, which the loader needs when the folder holds more than one.

An administrator with `packages.admin` installs it from a package remote, or
uploads a `.zip` of the package folder (`package.yaml` plus the listed files, all
at the top level) with **Upload plugin** on the portal's Plugins tab. Installing it
verifies each file's SHA-256, then, once the administrator ticks "I trust this
author" (`allow_plugin_code`), deposits the verified bytes into `PLUGIN_ID/` in the
installed-plugins folder. The server's `SHARPMUSH_PLUGIN_INSTALL` switch (on unless
set to `false`) decides whether it installs plugin packages at all. The plugin
**loads at the next start**; the Plugins tab's Restart button or `@shutdown/reboot`
restarts the engine without dropping client connections. Uninstalling removes the
folder (unloading first if the plugin is unloadable).

A plugin runs **arbitrary compiled C# in full server trust — there is no
sandbox**. SHA-256 guards integrity, not trust; whoever installs it takes
responsibility for it.

### The hashes are CI-filled

Do **not** hand-maintain the `sha256:` values in `package.yaml`. The
`build-and-release.yml` workflow does a deterministic Release build, computes the
SHA-256 of each deposited file, and rewrites the `binaries:` block. Commit the
hash-updated `package.yaml` in the **same commit** the release tag points at, so the
installer (which reads bytes from that exact commit) sees matching hashes.

## Publishing — the tag convention

Release as a git tag `vX.Y.Z` (single-package repo) or `PLUGIN_ID/vX.Y.Z` (monorepo,
the Go-modules convention). Bump `version:` in both `plugin.json` and `package.yaml`
to match. Release tags are **immutable** — never move one; a moved tag cannot smuggle
different bytes than the SHA-256 the manifest signed, but it will break the install.
