# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

SharpMUSH is a modern .NET 10 MUSH server (text-based multiplayer role-playing) targeting PennMUSH compatibility. The repository contains both the game engine and a Blazor WASM web portal. This branch (`feature/web-portal-design`) is focused on the web portal.

## Build & Test Commands

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

The startup project is `SharpMUSH.Server`. For full operation, also run `SharpMUSH.ConnectionServer`. Infrastructure (ArangoDB + NATS) is available via Docker:

```bash
docker compose up -d
```

Key environment variables:
- `SHARPMUSH_DATABASE_PROVIDER` — `arangodb` (default), `memgraph`, or `surrealdb`
- `ARANGO_CONNECTION_STRING` — ArangoDB connection string
- `MEMGRAPH_URI` — Bolt URI for Memgraph (default: `bolt://localhost:7687`)
- `NATS_URL` — NATS server URL (falls back to embedded Testcontainer in dev)

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
| `SharpMUSH.Database.ArangoDB` | ArangoDB provider (primary/default) |
| `SharpMUSH.Database.Memgraph` | Memgraph provider (Neo4j Bolt protocol) |
| `SharpMUSH.Database.SurrealDB` | SurrealDB embedded in-memory provider |
| `SharpMUSH.Messaging` | NATS pub/sub abstraction; Testcontainer fallback for dev |
| `SharpMUSH.Configuration` | Strongly-typed config options |
| `SharpMUSH.MarkupString` | ANSI/MXP markup string type used throughout the engine |
| `SharpMUSH.Tests` | TUnit tests (unit + integration with real DB via Testcontainers) |
| `SharpMUSH.Tests.BUnit` | bUnit component tests for Blazor pages and components |
| `SharpMUSH.Tests.Infrastructure` | Shared test helpers, `ServerWebAppFactory`, DB test servers |

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

A widget that accepts config must declare `ConfigType` and document its keys in `docs/guides/widget-configuration.md` — that guide is the reference admins read, and the layout editor offers only a raw JSON box for everything but the Spacer.

### Server-Side: Commands & Functions

Commands use `[SharpCommand]` attribute on static methods:
```csharp
[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL"], Behavior = CB.Default | CB.EqSplit)]
public static async ValueTask<Option<CallState>> Emit(IMUSHCodeParser parser, SharpCommandAttribute _2)
```

Functions use `[SharpFunction]` attribute:
```csharp
[SharpFunction(Name = "NAME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
public static ValueTask<CallState> Name(IMUSHCodeParser parser, SharpFunctionAttribute _2)
```

### Authentication Architecture

A custom `SharpAccount` model (not ASP.NET Identity) manages web accounts (email/password). Game characters retain MUSH passwords in the object DB, linked by `AccountId`. A single DB-backed account-session token authenticates both REST and SignalR via the `AccountSession` scheme (JWT + refresh cookie retired — see `docs/design/architectural-decisions.md` §1.2); roles/permissions resolve server-side (FusionCache) rather than being baked into a token. Bans revoke sessions and drop live connections immediately (`BanEnforcementService`). Sitelock (glob/CIDR) gates auth surfaces using trusted forwarded-headers client IPs; anonymous browsing stays open. `IAccountSessionStore` / `IOttStore` manage server-side state.

## Code Style

- **C# files**: tabs, indent size 2 — **enforced**, see below
- **Razor files**: spaces, indent size 4 (not enforced; `dotnet format` does not process `.razor`)
- **Line endings**: LF, pinned by `.gitattributes` (`* text=auto eol=lf`)
- `TreatWarningsAsErrors` is enabled in most projects, but not all — notably `SharpMUSH.Tests.BUnit` and `SharpMUSH.Tests.ScenePlugin` do not set it (the source-generated projects and `templates/` don't either). `SharpMUSH.Tests`, `SharpMUSH.Tests.Infrastructure`, and `SharpMUSH.Tests.Integration` DO set it. Check the specific `.csproj` before assuming either way.
- Prefer `var` throughout; no `this.` qualifier
- Discriminated unions via `OneOf<T1, T2>` (never nullable returns from services)
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
- **Use `--folder`, not the solution.** `dotnet format` cannot load `SharpMUSH.sln` on the
  .NET 11 SDK; the MSBuild build host crashes. Whitespace rules are syntactic, so folder mode
  loses nothing.

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

`docs/todo/area-NN-*.md` files track implementation status for each portal area.

## Infrastructure Notes

- **Logging**: Serilog; ArangoDB sink enabled in production for persistent logs
- **Metrics**: OpenTelemetry → Prometheus scraping at `/metrics` (server :9092, connection server :9091)
- **Caching**: `ZiggyCreatures.FusionCache`; compiled boolean-expression cache keyed as `"compiled-expressions"`
- **Rate limiting**: Fixed-window limiter on `"public-api"` (30 req/window); sliding-window on `"auth"` (10 req/window)
- **CORS**: Configured via `Cors:AllowedOrigins` in `appsettings.json`; development allows all origins with credentials (required for SignalR)
