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

**Tests share one world and one `NotifyService` substitute, and run in parallel.** A test that asserts what someone heard (or didn't hear) creates that receiver in a room of its own, not `DefaultHome`. It reads output through `TestHelpers.NotificationRecorder` (`Notifications` on the factory), matched on a unique string from `TestIsolationHelpers.GenerateUniqueName` (socket handles from `GenerateUniqueHandle`), never by position or "last line". It never calls `ClearReceivedCalls()` or `Received()` on the shared substitute.
Product code keeps per-engine settings off `static` fields; the test run starts several hosts in one process (#1245).
Only one host ever opens a given database. A host variant that needs a different configuration (`RealityGameServerFactory`) sets `UsesOwnWorld` and boots a world of its own; it never attaches to the session's (#1492).

## PennMUSH Parity Harness

`tools/parity/run.sh` replays the same scripted telnet sessions (DB import, login, player/admin commands, softcode) against a reference PennMUSH built from `pennmush/` and against SharpMUSH, and reports every output difference. Each parity fix adds a case to `tools/parity/scenarios/`. See `tools/parity/README.md` (normalization rules, known-differences allowlist, baseline).

## Running the Server

The startup project is `SharpMUSH.Server`. For full operation, also run `SharpMUSH.SocketServer` (client sockets) and `SharpMUSH.RenderingWorker` (output rendering). The compose stack runs all three on the embedded `lightning` provider with NATS, and is what `deploy/` ships to production:

```bash
docker compose up -d
```

The `lightning` provider is embedded and needs no Docker for the database itself — it
opens an LMDB directory in-process. NATS is still wanted for the connection server.

Key environment variables:
- `SHARPMUSH_DATABASE_PROVIDER` — accepts only `lightning` (the default when unset); any other value fails startup
- `SHARPMUSH_LIGHTNING_PATH` — LMDB data directory for the `lightning` provider (default: `lightning-data`)
- `SHARPMUSH_LIGHTNING_MAPSIZE` — LMDB map-size ceiling in bytes for the `lightning` provider (default: 64 GiB)
- `SHARPMUSH_BACKUP_PATH` — where `@backup` writes copies of the world (default: `<world path>.backups`)
- `SHARPMUSH_BACKUP_KEEP` — how many copies stay on disk (default: 2)
- `SHARPMUSH_BACKUP_PACKAGE_KEEP` — how many automatic copies taken before a portal package apply/rollback/uninstall stay, in `<backup path>/pre-package`, counted apart from the others (default: 2; `0` turns them off)
- `SHARPMUSH_BACKUP_INTERVAL` — how often a copy is taken automatically, e.g. `6h` (default: unset, no scheduled copy)
- `SHARPMUSH_LIGHTNING_BACKUP_COMPACT` — Lightning only; `false` to skip compaction, for faster and larger copies (default: on)
- `SHARPMUSH_LIGHTNING_SYNC` — how hard each LMDB commit pushes on the disk: `full` (default; every commit fsynced, nothing lost on power failure), `nometasync` (one fsync per commit instead of two; power failure can lose the last transaction), or `periodic` (no sync on commit; a timer forces one every `SHARPMUSH_LIGHTNING_FLUSH_MS`, default 1000, and power failure can lose at most that window). The file stays consistent in every mode.
- `SHARPMUSH_LIGHTNING_COMPACT_ON_START` — Lightning only; `true` replaces the world's file with a compacted copy before the server opens it, keeping the original at `<path>.precompact` (never deleted automatically; compaction is refused while one is there)
- `SHARPMUSH_HISTORY_<KIND>_KEEP` / `SHARPMUSH_HISTORY_<KIND>_MAX_AGE` — history retention per kind (`WIKI`, `SCENE_EDITS`; `SCENE_DELETED` and `AUDIT` take `_MAX_AGE` only); `SHARPMUSH_HISTORY_INTERVAL` schedules a pass, `SHARPMUSH_HISTORY_ARCHIVE_PATH` archives purged records first, `SHARPMUSH_HISTORY_BATCH` bounds one write. Every default keeps everything (see `deploy/README.md`, "History retention")
- `NATS_URL` — NATS server URL (falls back to embedded Testcontainer in dev)
- `SHARPMUSH_NATS_MAX_BYTES` — byte budget of each bus stream (`SHARPMUSH-CS`, `SHARPMUSH-MS`); a full stream refuses new publications (default: 512 MiB)
- `SHARPMUSH_NATS_MAX_AGE` — how long unconsumed bus messages wait for their consumer, e.g. `30m` (default: `1h`). Browser replay is configured apart: `Replay:RetentionHours`, `Replay:MaxBytes`, `Replay:MaxFrames` on the ConnectionServer. See `docs/design/messaging-retention.md`

Promoting a staged import under `lightning` renames the previous world to `<path>.previous`; it is not cleaned up automatically, so delete it once the promotion is verified.

`@storage` (wizard-only) reports the map limit, file length, allocated disk and live data apart, the backup run's peak and free-space need, and leftover worlds; the same figures are the `sharpmush_storage_*` gauges (`IStorageCapacityService`). A backup run checks free space before it writes (`WorldBackupWriter.EstimateCopyBytes`). `@storage/history` and `/purge` cover every registered `IHistoryStore` (the provider's wiki revisions and audit log, the Scene plugin's pose edits and deleted poses) through `IHistoryRetentionService`; a store judges candidates again inside its write job.

Under `lightning`, `@backup` (wizard-only) copies the live world into a timestamped directory using LMDB's own copy routine, so an external snapshot tool has a consistent one to read without the server stopping. `@backup/list` shows what is on disk. Nothing else copies a live `data.mdb` — see `deploy/README.md`.

`IWorldBackupService` is the provider-agnostic seam. `WorldBackupWriter` (in `SharpMUSH.Library`) owns everything provider-independent — writing the copy into `.incoming-<id>`, moving it into place only when complete, the `latest` symlink, retention — and the provider supplies only the part that fills a directory: for `lightning`, an `mdb_env_copy`.

The Lightning copy is point-in-time by construction.

First-run admin setup: web portal `/setup` (first visitor claims the pre-generated admin linked to `#1`); or set God's password in-game. After the claim the wizard stays pending (`api/setup/wizard`, expanded server data `SetupWizardState`) until the admin finishes it: optionally import a PennMUSH database with its `mush.cnf` (applied first), then set the HTTP and event handlers (`HandlerSetupService`: keep the game's own object, use another, create one, or none; the bundled packages attached to a handler move with it), then choose the bundled packages (`GameFeatureService.ApplyPackagesAsync`, dependencies added). A `mush.cnf` import keeps the running game's `http_handler`/`event_handler`/`package_manager` unless the file names them, and records the object references it did name (`MushCnfObjectReferences`) so a PennMUSH import keeps those instead of unsetting them with SharpMUSH's seeded #3-#9. An application is on exactly when its bundled package is installed (and its plugin loaded); removing a first-boot package records it in `DeclinedBundledPackages` so bootstrap does not reinstall it. `api/server-info` reports the enabled ids as `Features`, and the portal shows an application's links and pages only through `<FeatureGate Feature="@GameFeatures.X">`.

## Architecture

### Service Topology

```
Browser (Blazor WASM)
    │  REST /api/...
    │  SignalR /hubs/game
    └──► SharpMUSH.Server  (ASP.NET Core, port 8081 HTTPS)
              │  NATS pub/sub
              └──► SharpMUSH.SocketServer  (Telnet :4201, HTTP :4202)
                        │  HTTP/2 over a Unix socket ──► SharpMUSH.RenderingWorker
                        │  Telnet / WebSocket
                        └──► MU* clients
```

### Project Map

| Project | Role |
|---------|------|
| `SharpMUSH.Server` | ASP.NET Core host — REST API, SignalR hub, Blazor WASM file serving, middleware stack |
| `SharpMUSH.Client` | Blazor WASM web portal (MudBlazor 9.x) |
| `SharpMUSH.SocketServer` | Raw telnet/WebSocket gateway; owns client sockets and bridges to Server via NATS. Image `sharpmush-socketserver`, compose service `connectionserver` |
| `SharpMUSH.RenderingWorker` | Renders markup for a connection's protocol; restarts without dropping sockets. Image `sharpmush-connectionserver`, compose service `renderer` |
| `SharpMUSH.Library` | Core interfaces, models, service contracts (`ISharpDatabase`, all `I*Service`) |
| `SharpMUSH.Implementation` | MUSH parser (ANTLR4), commands, functions, substitutions |
| `SharpMUSH.Database.Lightning` | LMDB embedded provider through Lightning.NET; one directory per world; writes group-committed on one thread, sync policy per `SHARPMUSH_LIGHTNING_SYNC` |
| `SharpMUSH.Messaging` | NATS pub/sub abstraction; Testcontainer fallback for dev |
| `SharpMUSH.Configuration` | Strongly-typed config options |
| `SharpMUSH.Tests` | TUnit tests (unit + integration with real DB via Testcontainers) |
| `SharpMUSH.Tests.BUnit` | bUnit component tests for Blazor pages and components |
| `SharpMUSH.Tests.Infrastructure` | Shared test helpers, `ServerWebAppFactory`, DB test servers |

### Markup

Styled text is `MarkupText` (aliased as `MString` via `global using MString = global::MarkupString.MarkupText;` in each project's `GlobalUsings.cs`) — a plain string plus a set of coalesced, non-overlapping runs of `IMarkup` layers. It is **not in this repository**: it ships from [SharpMUSH/MarkupString](https://github.com/SharpMUSH/MarkupString) as five NuGet packages — `MarkupString` (the core, with no rendering opinions and the shared vocabulary's types), `MarkupString.Ansi` and `MarkupString.Html` (terminal styling and raw HTML tags), and `MarkupString.Mxp` and `MarkupString.Pueblo` (the two MUD dialects). Their version is pinned once, as `$(MarkupStringVersion)` in `Directory.Build.props`; every project that references them uses that property, because a mismatched set restores two copies of the core assembly.

A host wires up what it renders, once at startup. A host that writes to a MUD client needs all five — `MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp().WithPueblo();` (`SharpMUSH.Server`, `ConnectionServer`, the Tests bootstraps, Benchmarks). The portal is a browser and takes `WithAnsi().WithHtml(TagwrapPolicy.Portal)`; so does the LanguageServer. Leaving a dialect out is not an error the compiler sees — its markup simply renders as nothing for the client it exists for.

`MarkupFormat` has six values: `Plain`, `Ansi`, `Html`, `Pueblo`, `Mxp`, `BBCode` (plus `MarkupFormat.Custom(name, encoding)` for your own). A format carries its own text encoding, which is why `Pueblo` writes a line ending as `<BR>` and the others do not.

The **shared vocabulary** is how a game says a thing once and lets each format write it: `MarkupText.Sound`, `Music`, `StopSound`, `Image`, `Pane`, `ClearScreen`, `Prefetch`, `ExpireLinks`, `Variable`, `Gauge`, `Status`, `Relocate`, `LoginPrompt`, `Bell`. MXP gets `<SOUND>` and `<IMAGE>`, Pueblo its `xch_` tags, a browser `<audio>` and `<img>`, and a terminal the description or the text a picture or a pane stands in for. A command link is part of it too — build one with `AnsiMarkup.Create(linkUrl:, linkKind:)`, never a raw dialect tag. Several of these are *points*: they ride on a carrier character that `ToPlainText()`, `ToString()` and equality leave out, so a listen pattern never sees a sound. `Text` still holds the carrier, so `Length`, slicing and padding are unchanged — an emptiness check belongs on `Text`, not on the plain text.

`MString.ToString()` is always plain text (equivalent to `ToPlainText()`) — it is never format-specific. To produce output for a client, render explicitly: `text.Render(MarkupFormat.Ansi)`, `.Render(MarkupFormat.Html)`, etc. There is no `ToAnsi()`/`ToHtml()`.

MUSH-specific policy (space-list semantics, `compressSpaces`, glob-to-regex, column alignment, PennMUSH error strings) is not in the markup packages — it lives in `SharpMUSH.Library/Markup/` (`MushText`, `TextAligner`, `ColumnSpec`), built on top of the generic `MarkupText` API.

### Web Portal (SharpMUSH.Client)

The portal is a Blazor WASM app served by `SharpMUSH.Server` (SPA fallback: all non-API, non-file routes → `index.html`, sent `Cache-Control: no-cache`). A published image serves the assets through `MapStaticAssets` from the client's `SharpMUSH.Client.staticwebassets.endpoints.json`, copied into the server's content root by the `Dockerfile`: fingerprinted files get `immutable` caching and every asset its precompressed `.br`/`.gz`. Without the manifest (dev runs, test hosts) it falls back to `UseBlazorFrameworkFiles` + `UseStaticFiles`. See `SharpMUSH.Server/PortalStaticFiles.cs`.

Startup is on the critical path of every visit: `Program.cs` awaits nothing on the network before the
first render, and a request the first render needs goes out alongside the others rather than after
them.

Readiness is the server's business, not the portal's: until `ServerReadiness` is first true (host
started, every NATS input consumer consuming, the output bridge connected — the parts that come up
after Kestrel is already listening), a request for the SPA shell gets `PortalStartupPage` (503 +
`Retry-After`), so a portal that loaded is talking to a server that can play the game. `/api/health`
and `/ready` report the live state (503 while the broker is out); `/health` is liveness only. The
standalone client dev server (`dotnet run --project SharpMUSH.Client`, API via `ApiBaseAddress` in
`appsettings.Development.json`) bypasses that page: start the API first, or reload once it is up.

**Key services registered at startup:**

- `IWidgetRegistry` / `ILayoutService` — widget system; widgets registered at startup in `Program.cs`
- `ApplicationCatalog` — the Dynamic Applications snapshot; loads alongside the first render, so a reader that needs the whole list awaits `Loaded`
- `IThemeService` — built-in MudTheme accent presets, the choice persisted in localStorage (the CSS variables in `wwwroot/css/tokens.css` are static; see `docs/design/ui-patterns.md` §13)
- `WikiService` / `SceneService` — HTTP clients for the server's wiki and scene APIs (scene writes go through game commands)
- `GameCommandService` — runs a game command as the acting character (`POST api/commands`) and returns its output
- `IGameHubConnectionFactory` / `IConnectionStateService` — SignalR lifecycle management
- `AccountAuthService` — account-session token stored in WASM memory; mints per-character OTTs for the terminal
- `ITerminalService` / `IWebSocketClientService` — raw WebSocket terminal

**Authentication modes:**
- Development: `DebugAuthStateProvider` (bypasses auth, hardcoded wizard user)
- Production: `AccountAuthStateProvider` backed by the account session

**SignalR real-time flow:**
- Client connects to `/hubs/game` authenticated via the `AccountSession` token
- `GameHub` adds client to `char:{dbref}` group on connect
- Room events broadcast to `room:{dbref}` group
- The hub carries no commands. A command the portal issues itself goes through `POST api/commands`
  (`GameCommandService` → `CommandsController` → `PortalCommandService`): one line run as the account
  session's bound character — whatever the terminal is playing — on the engine's queue as typed input
  (`$`-commands in place), answered with the output that character was told while it ran (copied by
  `ICommandOutputCapture`; still delivered to its connections) and, when the request names one, a
  `Result` expression evaluated right after in the same queue entry, under the character's own output
  limit. A request naming a `Character` (objid) is refused 409 unless the session is still bound to it;
  an account may have `PortalCommands:MaxPendingPerAccount` (default 4) commands queued or running,
  and is answered 429 past that
- The Softcode Editor's console evaluates through `POST api/commands/eval` on the same queue and limit:
  an expression with `%0`-`%9`, as the character or, given an object the character controls, as that
  object with `%#` the character (how the editor runs its unsaved buffer the way `u()` would)

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

A custom `SharpAccount` model (not ASP.NET Identity) manages web accounts (email/password). Game characters retain MUSH passwords in the object DB, linked by `AccountId`. A single DB-backed account-session token authenticates both REST and SignalR via the `AccountSession` scheme (JWT + refresh cookie retired — see `docs/design/architectural-decisions.md` §1.2); roles/permissions resolve server-side (FusionCache) rather than being baked into a token. Objects and accounts both hold roles and overrides (`ObjectGrants`, read through `SharpObject.Grants`); a character also holds its account's. Permissions resolve the way Discord's do (`PermissionResolver`: owner, `administrator`, object override, account override, pooled role allows over denies, then `everyone`); priority only orders who may manage whom, never control. WIZARD/ROYALTY are the `wizard`/`royalty` roles and each built-in power a `game.<power>` scope (`RoleFlags`, `GamePowers`): privilege checks (`IsWizard`, `HasPower`, `Controls` via `control.all`/`protect.*`) read grants, never stored flags, and the provider writes those flags and powers as roles and overrides. Every change, from `@role` or `RolesController`, goes through `IRoleManagementService`. Bans revoke sessions and drop live connections immediately (`BanEnforcementService`). Sitelock (glob/CIDR) gates auth surfaces using trusted forwarded-headers client IPs; anonymous browsing stays open. `IAccountSessionStore` / `IOttStore` manage server-side state.

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
  with patterns (`x switch { T0 a => …, T1 b => … }`, `x is T t`), never by casting `Value`. C# does not
  narrow a union after `is not`, so "return the failure, carry on with the value" is a switch at the
  point the result is produced, with the rest of the work extracted into a method named for it:
  `return await CreateAsync(…) switch { SharpAccount account => await RegisteredAsync(account), Error<string> error => Conflict(error.Value) };`.
  When the failure's contents are not needed, a guard is enough: `if (x is not SharpAccount account) return NotFound();`.
  The optional object unions nest the object union (`AnyOptionalSharpObject(AnySharpObject, None)`, and the
  `…OrError`/`…Container`/`…Content` variants), so a lookup reads `if (found is not AnySharpObject obj) return …;`,
  and a concrete kind binds through the nesting: `found is AnySharpObject and SharpPlayer player`. Unions carry no
  member that throws on the wrong case — ask with a pattern, which binds the case in the same step.
  Tests bind the case they expect with `x.Expect<T>()` (Tests.Infrastructure), not `IsTypeOf<T>()` plus `!`.
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

### Claude Code Stop hook

`.claude/settings.json` registers `.claude/hooks/stop-verify.py` as a Stop hook. A turn cannot end
while a build or test job you started in the background is still running, while changed `.cs` files
fail the formatting gate above, or while a test project that depends on your changes fails. That
includes the client `node --test` suite when `SharpMUSH.Client/wwwroot/` changes. A pass is cached
until the changed files change again. When it blocks, fix the cause; don't disable the hook. Details
and the human escape hatch (`SHARPMUSH_STOP_HOOK=off`) are in `.claude/hooks/README.md`.

## Design Documents

`docs/design/` contains binding architectural decisions for the portal:
- `architectural-decisions.md` — auth, token strategy, permission roles, URL strategy
- `web-portal-vision.md` — feature overview and architecture diagram
- `implementation-order.md` — dependency graph for building portal features in parallel
- `url-strategy.md` — canonical route map (public, authenticated, admin, API)
- `engine-data-trunk.md` — engine reads/writes through the Mediator, stores, cache coherence, single-process assumptions
- `guided-input-ordering.md` — per-handle publication order; guided-input prompts and lifecycle notices display in commit order
- `messaging-retention.md` — bus vs replay retention, stream byte budgets, handler retry/terminate semantics, bounded replay reads

`docs/todo/area-NN-*.md` files track implementation status for each portal area.

## Infrastructure Notes

- **Logging**: Serilog, configured through `appsettings.json`
- **Metrics**: OpenTelemetry → Prometheus scraping at `/metrics` (server :9092, connection server :9091)
- **Caching**: `ZiggyCreatures.FusionCache`; compiled boolean-expression cache keyed as `"compiled-expressions"`
- **Rate limiting**: `"public-api"` — fixed window per client IP (30 req/min, `RateLimiting:PublicApi:*`) on the credential/claim endpoints that opt in with `[EnableRateLimiting]`; `"mcp"` — per client IP on `/mcp`; `"softcode-http"` — one global `http_per_second` budget on `/http/*`. Portal assets, the boot reads (`api/setup/status`, `api/server-info`) and `api/help` are not limited
- **CORS**: Configured via `Cors:AllowedOrigins` in `appsettings.json`; development allows all origins with credentials (required for SignalR)
