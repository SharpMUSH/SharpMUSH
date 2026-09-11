# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

SharpMUSH is a modern .NET 11 MUSH server (text-based multiplayer role-playing) targeting PennMUSH compatibility. The repository contains both the game engine and a Blazor WASM web portal.

## Build & Test Commands

`global.json` pins the SDK to **11.0.100-rc.1** (`allowPrerelease: true`, rolling forward within
the 11.0.1xx band), so a .NET 10 SDK or an older 11.0 preview will not satisfy it — `dotnet` fails
with "A compatible .NET SDK was not found" before any project is read. Install it with:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 11.0.100-rc.1.26425.128
```

The source-generator projects reference `Microsoft.CodeAnalysis.CSharp` 5.9.0; the compiler that
loads them must be at least that version, and 11.0.100-rc.1 carries Roslyn 5.11. An SDK with an
older compiler rejects the generators with CS9057.

`global.json` is the only place the SDK version is written down. Every workflow resolves it with
`actions/setup-dotnet`'s `global-json-file: global.json`, so bumping it is a one-line change here;
the Dockerfiles track the floating `mcr.microsoft.com/dotnet/sdk:11.0` tag and fail loudly against
`global.json` if that tag ever lags the pin. The runtime-versioned packages (`Microsoft.AspNetCore.*`,
`Microsoft.Extensions.*`, `Microsoft.Data.Sqlite`) share one version, `$(DotNetPackageVersion)` in
`Directory.Build.props`, which moves with it.

```bash
# Build everything
dotnet build

# Run all tests
dotnet test
# or (preferred for TUnit output)
dotnet run --project SharpMUSH.Tests

# Run a specific test class
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/BuildingCommandTests/*"

# Run a single test
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/BuildingCommandTests/CreateObject"

# Run with verbose output
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MyTests/*" --output detailed

# Run Blazor component tests (bUnit + TUnit)
dotnet run --project SharpMUSH.Tests.BUnit

# Enable console logging during tests (off by default)
SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=true dotnet run --project SharpMUSH.Tests

# Enable telemetry summary after tests
SHARPMUSH_ENABLE_TEST_TELEMETRY=true dotnet run --project SharpMUSH.Tests
```

The test framework is **TUnit** (not xUnit or MSTest). The `--treenode-filter` format is `/<assembly>/<namespace>/<class>/<method>` with `*` wildcards.

## Running the Server

The startup project is `SharpMUSH.Server`. For full operation, also run `SharpMUSH.ConnectionServer`. The compose stack runs both on the embedded `lightning` provider with NATS, and is what `deploy/` ships to production:

```bash
docker compose up -d
```

Both supported providers are embedded and need no Docker for the database itself — `lightning`
opens an LMDB directory in-process. NATS is still wanted for the connection server.

Key environment variables:
- `SHARPMUSH_DATABASE_PROVIDER` — `lightning` (default) or `surrealdb`
- `SHARPMUSH_LIGHTNING_PATH` — LMDB data directory for the `lightning` provider (default: `lightning-data`)
- `SHARPMUSH_LIGHTNING_MAPSIZE` — LMDB map-size ceiling in bytes for the `lightning` provider (default: 64 GiB)
- `SHARPMUSH_BACKUP_PATH` — where `@backup` writes copies of the world (default: `<world path>.backups`; required under `surrealdb` on a `mem://` endpoint, since it has no world directory to derive it from)
- `SHARPMUSH_BACKUP_KEEP` — how many copies stay on disk (default: 2)
- `SHARPMUSH_BACKUP_INTERVAL` — how often a copy is taken automatically, e.g. `6h` (default: unset, no scheduled copy)
- `SHARPMUSH_LIGHTNING_BACKUP_COMPACT` — Lightning only; `false` to skip compaction, for faster and larger copies (default: on)
- `SHARPMUSH_LIGHTNING_SYNC` — how hard each LMDB commit pushes on the disk: `full` (default; every commit fsynced, nothing lost on power failure), `nometasync` (one fsync per commit instead of two; power failure can lose the last transaction), or `periodic` (no sync on commit; a timer forces one every `SHARPMUSH_LIGHTNING_FLUSH_MS`, default 1000, and power failure can lose at most that window). The file stays consistent in every mode.
- `NATS_URL` — NATS server URL (falls back to embedded Testcontainer in dev)

Promoting a staged import under `lightning` renames the previous world to `<path>.previous`; it is not cleaned up automatically, so delete it once the promotion is verified.

Under `lightning`, `@backup` (wizard-only) copies the live world into a timestamped directory using LMDB's own copy routine, so an external snapshot tool has a consistent one to read without the server stopping. `@backup/list` shows what is on disk. Nothing else copies a live `data.mdb` — see `deploy/README.md`.

`IWorldBackupService` is the provider-agnostic seam. `WorldBackupWriter` (in `SharpMUSH.Library`) owns everything identical across providers — writing the copy into `.incoming-<id>`, moving it into place only when complete, the `latest` symlink, retention — and each provider supplies only the part that fills a directory: `lightning` an `mdb_env_copy`, and `surrealdb` a `world.surql` export.

Only the Lightning copy is point-in-time by construction; the other two are logical dumps taken from a running game. Don't describe them as equivalent.

First-run admin setup: web portal `/setup` (first visitor claims the pre-generated admin linked to `#1`); or set God's password in-game.

## Architecture

### Service Topology

```
Browser (Blazor WASM)
    │  REST /api/...
    │  SignalR /hubs/game
    └──► SharpMUSH.Server  (ASP.NET Core, port 8081 HTTPS)
              │  NATS pub/sub
              └──► SharpMUSH.ConnectionServer  (Telnet :4201, HTTP :4202)
                        │  Telnet / WebSocket
                        └──► MU* clients
```

### Project Map

| Project | Role |
|---------|------|
| `SharpMUSH.Server` | ASP.NET Core host — REST API, SignalR hub, Blazor WASM file serving, middleware stack |
| `SharpMUSH.Client` | Blazor WASM web portal (MudBlazor 9.x) |
| `SharpMUSH.ConnectionServer` | Raw telnet/WebSocket gateway; bridges to Server via NATS |
| `SharpMUSH.Library` | Core interfaces, models, service contracts (`ISharpDatabase`, all `I*Service`) |
| `SharpMUSH.Implementation` | MUSH parser (ANTLR4), commands, functions, substitutions |
| `SharpMUSH.Database.SurrealDB` | SurrealDB embedded provider (RocksDB on disk in production, in-memory in tests) |
| `SharpMUSH.Database.Lightning` | LMDB embedded provider through Lightning.NET; one directory per world; writes group-committed on one thread, sync policy per `SHARPMUSH_LIGHTNING_SYNC` |
| `SharpMUSH.Messaging` | NATS pub/sub abstraction; Testcontainer fallback for dev |
| `SharpMUSH.Configuration` | Strongly-typed config options |
| `SharpMUSH.Tests` | TUnit tests (unit + integration with real DB via Testcontainers) |
| `SharpMUSH.Tests.BUnit` | bUnit component tests for Blazor pages and components |
| `SharpMUSH.Tests.Infrastructure` | Shared test helpers, `ServerWebAppFactory`, DB test servers |

### Markup

Styled text is `MarkupText` (aliased as `MString` via `global using MString = global::MarkupString.MarkupText;` in each project's `GlobalUsings.cs`) — a plain string plus a set of coalesced, non-overlapping runs of `IMarkup` layers. It is **not in this repository**: it ships from [SharpMUSH/MarkupString](https://github.com/SharpMUSH/MarkupString) as three NuGet packages — `MarkupString` (the core, with no rendering opinions), `MarkupString.Ansi` and `MarkupString.Html` (the two markup kinds). Their version is pinned once, as `$(MarkupStringVersion)` in `Directory.Build.props`; the thirteen projects that reference them all use that property, because a mismatched trio restores two copies of the core assembly. Every host wires the two kinds into the registry once at startup: `MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();` (`SharpMUSH.Server`, `ConnectionServer`, `Client`, the Tests infrastructure, Benchmarks, LanguageServer). `MarkupFormat` has six values: `Plain`, `Ansi`, `Html`, `Pueblo`, `Mxp`, `BBCode` (plus `MarkupFormat.Custom(name, encoding)` for your own).

`MString.ToString()` is always plain text (equivalent to `ToPlainText()`) — it is never format-specific. To produce output for a client, render explicitly: `text.Render(MarkupFormat.Ansi)`, `.Render(MarkupFormat.Html)`, etc. There is no `ToAnsi()`/`ToHtml()`.

MUSH-specific policy (space-list semantics, `compressSpaces`, glob-to-regex, column alignment, PennMUSH error strings) is not in the markup packages — it lives in `SharpMUSH.Library/Markup/` (`MushText`, `TextAligner`, `ColumnSpec`), built on top of the generic `MarkupText` API.

### Web Portal (SharpMUSH.Client)

The portal is a Blazor WASM app served by `SharpMUSH.Server` (SPA fallback: all non-API routes → `index.html`).

**Key services registered at startup:**

- `IWidgetRegistry` / `ILayoutService` — widget system; widgets registered at startup in `Program.cs`
- `IThemeService` — DB-backed MudTheme + CSS variables
- `IWikiService` (via `InMemoryWikiService`) — wiki CRUD
- `ISceneService` (via `InMemorySceneService`) — real-time scene participation
- `IGameHubConnectionFactory` / `IConnectionStateService` — SignalR lifecycle management
- `AccountAuthService` — account-session token stored in WASM memory; mints per-character OTTs for the terminal
- `ITerminalService` / `IWebSocketClientService` — raw WebSocket terminal

**Authentication modes:**
- Development: `DebugAuthStateProvider` (bypasses auth, hardcoded wizard user)
- Production: `AccountAuthStateProvider` backed by the account session

**SignalR real-time flow:**
- Client connects to `/hubs/game` authenticated via the `AccountSession` token
- `GameHub` adds client to `char:{dbref}` group on connect
- Client calls `SendCommand` → NATS → engine → NATS → `ReceiveOutput` back to client
- Room events broadcast to `room:{dbref}` group

### Widget System

Widgets are composable portal units. Adding a new widget requires:

1. Create a Razor component in `SharpMUSH.Client/Components/Widgets/`
2. Create a descriptor class implementing `IPortalWidget` in `SharpMUSH.Client/Widgets/`
3. Register it in `Program.cs`: `registry.Register(new MyWidgetDescriptor())`

`IPortalWidget` declares: `Name` (machine key), `ComponentType` (Razor type), `ConfigType` (optional config model), `AllowedZones`, `DefaultSize`.

A widget that accepts config must declare `ConfigType` and tag each property of that model with `[WidgetConfigKey("Lay…")]`, naming a `SharedResource` key for its description. The layout editor generates its key reference from those via `WidgetConfigSchema`, so an untagged property is configurable but undocumented — and tests fail a `ConfigType` documenting no keys, or a description key missing from the resx. Add worked examples to `docs/guides/widget-configuration.md`.

### Server-Side: Commands & Functions

Commands use `[SharpCommand]` attribute on instance methods of the partial `Commands` class:
```csharp
[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL"], Behavior = CB.Default | CB.EqSplit)]
public async ValueTask<Option<CallState>> Emit(IMUSHCodeParser parser, SharpCommandAttribute _2)
```

Functions use `[SharpFunction]` attribute on instance methods of the partial `Functions` class:
```csharp
[SharpFunction(Name = "NAME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
public ValueTask<CallState> Name(IMUSHCodeParser parser, SharpFunctionAttribute _2)
```

Attribute triads go through `IDidItService`, never hand-rolled: `DidIt` for the
message/o-message/action trio (PennMUSH's `real_did_it`) and `FailLock` for a failed lock
(`fail_lock`). The o-message is evaluated once with the attribute's holder as executor, and the
action attribute is queued rather than run inline. Movement is `IMoveService.EnterRoom` /
`SafeTel`; no command sends `MoveObjectCommand` directly, and that command requires the origin
container so a move can never expire every container's contents. The automatic look after a move is
`ILookService.LookRoom(..., LookKey.Auto)` — commands do not queue a `look` of their own.

`Commands` and `Functions` are DI-constructed; their services are non-null members (`Mediator`,
`NotifyService`, ...), never static and never null-forgiven. The source generators emit
`CommandLibrary.Create(instance)` / `FunctionLibrary.Create(instance)` for instance methods and a
static table for static methods, which is what plugin modules use.

### Data trunk, stores and cache

`docs/design/engine-data-trunk.md` is binding. In short: every read and write of game state is a
Mediator request carrying its own cache policy (`ICacheable` / `ICacheInvalidating` /
`ICacheInvalidatingByResult<T>`); handlers depend on the per-aggregate store they use
(`IObjectStore`, `IAttributeStore`, `INavigationStore`, ... in `SharpMUSH.Library/Stores`), not on
`ISharpDatabase`; no handler holds an `IFusionCache`; providers take no Mediator and no cache;
a constructor cycle is broken with `Lazy<T>` (registered as an open generic), not with a request
whose handler calls a service; migration is awaited once in `Program` right after the host is built, before any service that reads the database is resolved.

### Authentication Architecture

A custom `SharpAccount` model (not ASP.NET Identity) manages web accounts (email/password). Game characters retain MUSH passwords in the object DB, linked by `AccountId`. A single DB-backed account-session token authenticates both REST and SignalR via the `AccountSession` scheme (JWT + refresh cookie retired — see `docs/design/architectural-decisions.md` §1.2); roles/permissions resolve server-side (FusionCache) rather than being baked into a token. Bans revoke sessions and drop live connections immediately (`BanEnforcementService`). Sitelock (glob/CIDR) gates auth surfaces using trusted forwarded-headers client IPs; anonymous browsing stays open. `IAccountSessionStore` / `IOttStore` manage server-side state.

## Code Style

- **C# files**: tabs, indent size 2 — **enforced**, see below
- **Razor files**: spaces, indent size 4 (not enforced; `dotnet format` does not process `.razor`)
- **Line endings**: LF, pinned by `.gitattributes` (`* text=auto eol=lf`)
- `TreatWarningsAsErrors` is enabled in most projects, but not all — notably `SharpMUSH.Tests.BUnit` and `SharpMUSH.Tests.ScenePlugin` do not set it (the source-generated projects and `templates/` don't either). `SharpMUSH.Tests`, `SharpMUSH.Tests.Infrastructure`, and `SharpMUSH.Tests.Integration` DO set it. Check the specific `.csproj` before assuming either way.
- Prefer `var` throughout; no `this.` qualifier
- Discriminated unions are C# 15 unions, never nullable returns from services. An inline result is a
  `union` declaration (a struct): reuse `Result<T>` (value or `Error<string>`), `Found<T>` (value or
  `NotFound`) or `FoundResult<T>` from `SharpMUSH.Contracts` before declaring one, and name a new one
  for what it means. A union handed around as a nullable reference (`AnySharpObject?`, `Option<T>`)
  is a `[Union] sealed partial class : IUnion`. The case primitives (`None`, `NotFound`, `Success`,
  `Error`, `Error<T>`) are record structs in `SharpMUSH.Library.DiscriminatedUnions`. Consume a union
  with patterns (`x switch { T0 a => …, T1 b => … }`, `x is T t`); the positional
  `IsT0`/`AsT0`/`Match`/`Switch`/`FromT0` members in the `Compat/` folders are migration scaffolding
  — add no new callers.
- Source-generated `Mediator` (not MediatR) for command/query dispatching

### Formatting is enforced

Two gates, both running `dotnet format whitespace` against `.editorconfig`:

| Gate | Where | Scope | Cost |
|---|---|---|---|
| `VerifyEditorConfigFormatting` (`Directory.Build.targets`) | every local build | the project being built | ~0.8s, and only when that project's `.cs` files changed — a no-op rebuild pays nothing |
| `format` job (`_dotnet-build-test.yml`) | CI | whole repo, one pass | ~2s |

If a build fails with **`FORMAT001`**, fix it with the command in the error text:

```bash
dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"
```

Run it until it reports no changes — **the formatter needs two passes to converge**.

Escape hatch: `-p:SkipFormatVerification=true`. CI's build steps set it because the `format`
job already covers everything.

Two things to know before changing this:

- **`IDE0055` does not work here.** `EnforceCodeStyleInBuild` with
  `dotnet_diagnostic.IDE0055.severity = error` reports nothing for indentation on this SDK —
  verified against a file with 93 space-indented lines, which built clean. It looks like a
  gate and enforces nothing. That is why the check shells out to `dotnet format` instead.
- **Use `--folder`, not the solution.** Whitespace rules are syntactic, so folder mode loses
  nothing, and it does not load MSBuild projects at all: ~2s for the whole repo against ~14s
  for `SharpMUSH.sln`. Solution mode is also unsafe as a gate — it exits 0 even when MSBuild
  fails to load a project, silently dropping that project's files from the check.

`csharp_new_line_before_members_in_object_initializers = false` (`.editorconfig:13`) is **not**
honoured by folder-mode formatting — it is a semantic option. Compact anonymous-object
initializers will be expanded to one member per line by the formatter, and re-compacting them
fails the gate.

## Design Documents

`docs/design/` contains binding architectural decisions for the portal:
- `architectural-decisions.md` — auth, token strategy, permission roles, URL strategy
- `web-portal-vision.md` — feature overview and architecture diagram
- `implementation-order.md` — dependency graph for building portal features in parallel
- `url-strategy.md` — canonical route map (public, authenticated, admin, API)
- `engine-data-trunk.md` — engine reads/writes through the Mediator, stores, cache coherence, single-process assumptions

`docs/todo/area-NN-*.md` files track implementation status for each portal area.

## Infrastructure Notes

- **Logging**: Serilog, configured through `appsettings.json`
- **Metrics**: OpenTelemetry → Prometheus scraping at `/metrics` (server :9092, connection server :9091)
- **Caching**: `ZiggyCreatures.FusionCache`; compiled boolean-expression cache keyed as `"compiled-expressions"`
- **Rate limiting**: Fixed-window limiter on `"public-api"` (30 req/window); sliding-window on `"auth"` (10 req/window)
- **CORS**: Configured via `Cors:AllowedOrigins` in `appsettings.json`; development allows all origins with credentials (required for SignalR)
