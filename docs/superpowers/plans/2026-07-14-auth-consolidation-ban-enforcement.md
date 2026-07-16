# Auth Consolidation & Immediate Ban Enforcement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `@sitelock` host/IP bans and account disables take effect immediately across telnet, websocket, and SignalR — revoking sessions and dropping live connections within seconds — by consolidating the web credential onto a single DB-backed, revocable account-session token and retiring the JWT/refresh hybrid.

**Architecture:** The opaque account-session token becomes the sole web credential, stored in the DB with an origin IP. A new `AccountSession` authentication handler validates it for REST and SignalR, resolving roles/permissions server-side (FusionCache) so bans land on the next request. A `BanEnforcementService` chokepoint revokes matching sessions, publishes `DisconnectConnectionMessage` per matching telnet/websocket handle over the existing NATS channel, and aborts matching SignalR connections. A shared `SitelockMatcher` (glob + CIDR) gates auth surfaces; `UseForwardedHeaders` makes the web-side client IP trustworthy.

**Tech Stack:** .NET 10, ASP.NET Core (auth handlers, SignalR, forwarded headers), source-generated Mediator, FusionCache (ZiggyCreatures), NATS (via `IMessageBus`), ArangoDB/SurrealDB/Memgraph providers, TUnit + Testcontainers (Podman), bUnit.

**Spec:** `docs/superpowers/specs/2026-07-14-auth-consolidation-ban-enforcement-design.md`

## Global Constraints

- C# files: tabs, indent size 2. Razor: spaces, indent 4. `TreatWarningsAsErrors` on in every project.
- Prefer `var`; no `this.`; `OneOf<T1,T2>` for service results (never nullable for error cases); source-generated `Mediator` (not MediatR).
- Test framework is **TUnit**. Run: `dotnet run --project <TestProject> -- --treenode-filter "/*/*/<Class>/<Method>"`. Testcontainers run via **Podman** (works on this machine — never claim unavailable).
- Account IDs are normalized `node_accounts/<key>` across all providers. Arango docs use **PascalCase** fields; Surreal/Memgraph use **camelCase** (SurrealDb.Net matches record property names case-sensitively, ignores `[JsonPropertyName]`).
- Session token format stays `Guid.NewGuid().ToString("N")`. Account-session TTL is 15 minutes, sliding on each successful validate. OTT TTL 60s single-use (unchanged, in-memory).
- The single web auth scheme is `AccountSession` (new). `DebugAuth` stays the dev default when no real credential is present. `MushBasic` (MCP) is unchanged.
- Client IP for web requests comes from `HttpContext.Connection.RemoteIpAddress` **after** `UseForwardedHeaders` resolves `X-Forwarded-For`/`CF-Connecting-IP`. Telnet/websocket IPs arrive via ConnectionServer metadata key `InternetProtocolAddress`.
- The NATS drop channel is the existing `record DisconnectConnectionMessage(long Handle, string? Reason) : IHandleMessage`, published via `IMessageBus.Publish(...)`, consumed by `DisconnectConnectionConsumer` → `connectionService.DisconnectAsync(handle)`.
- `GameHub.CharacterDbrefClaim = "character_dbref"` with value `#{key}` — the claim GameHub authorizes on.
- Anonymous web browsing is NEVER gated by sitelock — only auth surfaces and game-connection handshakes.
- New user-facing socket messages use plain `NotifyService.Notify(...)` strings (localization is an accepted follow-up).

---

# Phase 1 — DB-backed session store (prerequisite)

### Task 1: Session origin IP + revoke-by-IP on the in-memory store

**Files:**
- Modify: `SharpMUSH.Library/Services/Interfaces/IAccountSessionStore.cs`
- Modify: `SharpMUSH.Library/Services/InMemoryAccountSessionStore.cs`
- Modify call sites (add `originIp`): `SharpMUSH.Server/Controllers/AuthController.cs` (`AccountLogin`, `AccountRegister`, `GetDebugOtt`), `SharpMUSH.Server/Services/SetupService.cs` (wherever it mints a session — grep `CreateTokenAsync`)
- Test: `SharpMUSH.Tests/Services/InMemoryAccountSessionStoreTests.cs`

**Interfaces:**
- Produces (later tasks depend on these EXACT signatures):
  - `IAccountSessionStore.CreateTokenAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default)` → `Task<string>` (adds `originIp`)
  - `IAccountSessionStore.RevokeAllForIpAsync(string originIp, CancellationToken ct = default)` → `Task`
  - existing `ValidateAsync`, `RevokeAsync`, `RevokeAllForAccountAsync` unchanged.

- [ ] **Step 1: Write failing tests**

Append to `InMemoryAccountSessionStoreTests.cs`:

```csharp
	[Test]
	public async Task RevokeAllForIp_RemovesOnlyThatIpsTokens()
	{
		var store = new InMemoryAccountSessionStore();
		var t1 = await store.CreateTokenAsync("acct-A", TimeSpan.FromMinutes(15), "203.0.113.7");
		var t2 = await store.CreateTokenAsync("acct-B", TimeSpan.FromMinutes(15), "203.0.113.7");
		var t3 = await store.CreateTokenAsync("acct-C", TimeSpan.FromMinutes(15), "198.51.100.2");

		await store.RevokeAllForIpAsync("203.0.113.7");

		await Assert.That(await store.ValidateAsync(t1)).IsNull();
		await Assert.That(await store.ValidateAsync(t2)).IsNull();
		await Assert.That(await store.ValidateAsync(t3)).IsEqualTo("acct-C");
	}
```

Fix the existing tests in this file that call `CreateTokenAsync(accountId, ttl)` — add a dummy IP arg `"0.0.0.0"`.

- [ ] **Step 2: Verify failure** — `dotnet build SharpMUSH.Tests 2>&1 | tail` → compile error (arg count / missing method).

- [ ] **Step 3: Implement**

`IAccountSessionStore.cs`:

```csharp
	Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default);
	Task<string?> ValidateAsync(string token, CancellationToken ct = default);
	Task RevokeAsync(string token, CancellationToken ct = default);
	Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default);
	Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default);
```

`InMemoryAccountSessionStore.cs` — extend `Entry` and add the method:

```csharp
	private readonly record struct Entry(string AccountId, DateTimeOffset Expiry, TimeSpan Ttl, string OriginIp);

	public Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		_tokens[token] = new Entry(accountId, DateTimeOffset.UtcNow.Add(ttl), ttl, originIp);
		return Task.FromResult(token);
	}

	public Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default)
	{
		foreach (var pair in _tokens.Where(p => p.Value.OriginIp == originIp))
			_tokens.TryRemove(pair.Key, out _);
		return Task.CompletedTask;
	}
```

`ValidateAsync`'s `entry with { Expiry = ... }` keeps `OriginIp` automatically.

- [ ] **Step 4: Update call sites**

In `AuthController`, add a private helper and use it:

```csharp
	private string ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
```

Change every `accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15))` to `accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), ClientIp())` (in `AccountLogin`, `AccountRegister`, `GetDebugOtt`). In `SetupService` (grep its `CreateTokenAsync`), thread the IP through — if `SetupController.Complete` mints the session, resolve `HttpContext.Connection.RemoteIpAddress` there and pass it in.

- [ ] **Step 5: Run tests + build**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/InMemoryAccountSessionStoreTests/*"` → PASS
Run: `dotnet build` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Library/Services/ SharpMUSH.Server/Controllers/AuthController.cs SharpMUSH.Server/Services/SetupService.cs SharpMUSH.Tests/Services/InMemoryAccountSessionStoreTests.cs
git commit -m "feat: session origin IP + revoke-by-IP on the in-memory session store"
```

---

### Task 2: `SharpSession` model, `ISharpDatabase` session methods, ArangoDB impl + migration

**Files:**
- Create: `SharpMUSH.Library/Models/SharpSession.cs`
- Modify: `SharpMUSH.Library/ISharpDatabase.cs` (new `#region Session Methods` after Server State region)
- Modify: `SharpMUSH.Database/DatabaseConstants.cs` (add `Sessions`)
- Create: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Sessions.cs`
- Create: `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddSessions.cs`
- Add temporary `NotImplementedException` stubs in `SharpMUSH.Database.SurrealDB/SurrealDatabase.ServerState.cs` and `SharpMUSH.Database.Memgraph/MemgraphDatabase.ServerState.cs` (replaced in Task 3 — plan-mandated so the solution builds)
- Test: `SharpMUSH.Tests/Database/SessionStoreDbTests.cs`

**Interfaces:**
- Produces:
  - `SharpSession { string Token; string AccountId; long ExpiryUnixMs; long TtlMs; string OriginIp }`
  - `ISharpDatabase.UpsertSessionAsync(SharpSession session, ct)` → `ValueTask`
  - `ISharpDatabase.GetSessionAsync(string token, ct)` → `ValueTask<SharpSession?>`
  - `ISharpDatabase.DeleteSessionAsync(string token, ct)` → `ValueTask`
  - `ISharpDatabase.DeleteSessionsForAccountAsync(string accountId, ct)` → `ValueTask`
  - `ISharpDatabase.DeleteSessionsForIpAsync(string originIp, ct)` → `ValueTask`
  - `DatabaseConstants.Sessions = "node_sessions"`; collection keyed by `_key = token`.

- [ ] **Step 1: Write failing test**

`SessionStoreDbTests.cs` (mirror `ServerStateTests.cs` fixture):

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class SessionStoreDbTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static SharpSession Make(string token, string acct, string ip) => new()
	{
		Token = token, AccountId = acct, OriginIp = ip,
		ExpiryUnixMs = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeMilliseconds(),
		TtlMs = (long)TimeSpan.FromMinutes(15).TotalMilliseconds
	};

	[Test, NotInParallel(nameof(SessionStoreDbTests))]
	public async Task Upsert_Get_Delete_RoundTrip()
	{
		var s = Make("tok-rt-1", "node_accounts/1", "203.0.113.9");
		await Db.UpsertSessionAsync(s);
		var got = await Db.GetSessionAsync("tok-rt-1");
		await Assert.That(got).IsNotNull();
		await Assert.That(got!.AccountId).IsEqualTo("node_accounts/1");
		await Assert.That(got.OriginIp).IsEqualTo("203.0.113.9");

		await Db.DeleteSessionAsync("tok-rt-1");
		await Assert.That(await Db.GetSessionAsync("tok-rt-1")).IsNull();
	}

	[Test, NotInParallel(nameof(SessionStoreDbTests))]
	public async Task DeleteForAccount_And_ForIp()
	{
		await Db.UpsertSessionAsync(Make("tok-a1", "acctX", "10.0.0.1"));
		await Db.UpsertSessionAsync(Make("tok-a2", "acctX", "10.0.0.2"));
		await Db.UpsertSessionAsync(Make("tok-b1", "acctY", "10.0.0.1"));

		await Db.DeleteSessionsForAccountAsync("acctX");
		await Assert.That(await Db.GetSessionAsync("tok-a1")).IsNull();
		await Assert.That(await Db.GetSessionAsync("tok-a2")).IsNull();
		await Assert.That(await Db.GetSessionAsync("tok-b1")).IsNotNull();

		await Db.DeleteSessionsForIpAsync("10.0.0.1");
		await Assert.That(await Db.GetSessionAsync("tok-b1")).IsNull();
	}
```

- [ ] **Step 2: Verify failure** — build error (types undefined).

- [ ] **Step 3: Model + interface + constant**

`SharpSession.cs`:

```csharp
namespace SharpMUSH.Library.Models;

/// <summary>
/// A persisted web account session. The token is the primary key; sessions are
/// revoked (deleted) instantly by token, account, or origin IP for ban enforcement.
/// </summary>
public class SharpSession
{
	public required string Token { get; set; }
	public required string AccountId { get; set; }
	public long ExpiryUnixMs { get; set; }
	public long TtlMs { get; set; }
	public required string OriginIp { get; set; }
}
```

`ISharpDatabase.cs`, after the Server State `#endregion`:

```csharp
	#region Session Methods

	/// <summary>Creates or replaces a session document keyed by its token.</summary>
	ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default);

	/// <summary>Returns the session for a token, or null if absent.</summary>
	ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default);

	ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default);
	ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default);
	ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default);

	#endregion
```

`DatabaseConstants.cs` near `ServerState`:

```csharp
	// Web account sessions — one document per token; deleted on revoke/ban.
	public const string Sessions = "node_sessions";
```

- [ ] **Step 4: ArangoDB impl** (`ArangoDatabase.Sessions.cs`, mirror `ArangoDatabase.ServerState.cs` idiom, PascalCase fields):

```csharp
using System.Text.Json;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Sessions },
				{ "key", session.Token },
				{ "doc", new Dictionary<string, object?>
					{
						["_key"] = session.Token,
						["AccountId"] = session.AccountId,
						["ExpiryUnixMs"] = session.ExpiryUnixMs,
						["TtlMs"] = session.TtlMs,
						["OriginIp"] = session.OriginIp
					} }
			}, cancellationToken: cancellationToken);
	}

	public async ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR d IN @@c FILTER d._key == @key RETURN d",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "key", token } },
			cancellationToken: cancellationToken);
		if (result.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } e) return null;
		return new SharpSession
		{
			Token = token,
			AccountId = e.GetProperty("AccountId").GetString()!,
			ExpiryUnixMs = e.GetProperty("ExpiryUnixMs").GetInt64(),
			TtlMs = e.GetProperty("TtlMs").GetInt64(),
			OriginIp = e.GetProperty("OriginIp").GetString()!
		};
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
		=> await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d._key == @key REMOVE d IN @@c",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "key", token } },
			cancellationToken: cancellationToken);

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.AccountId == @a REMOVE d IN @@c",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "a", accountId } },
			cancellationToken: cancellationToken);

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
		=> await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.OriginIp == @ip REMOVE d IN @@c",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "ip", originIp } },
			cancellationToken: cancellationToken);
}
```

- [ ] **Step 5: Migration** (`Migration_AddSessions.cs`, mirror `Migration_AddServerState.cs`; Id `20260714_001`):

```csharp
using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

public class Migration_AddSessions : IArangoMigration
{
	public long Id => 20260714_001;
	public string Name => "add_sessions";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.Sessions))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.Sessions,
				Type = ArangoCollectionType.Document
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
```

- [ ] **Step 6: Temporary provider stubs** — add to `SurrealDatabase.ServerState.cs` and `MemgraphDatabase.ServerState.cs` (replaced in Task 3):

```csharp
	public ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
```

- [ ] **Step 7: Run + commit**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SessionStoreDbTests/*"` → PASS (ArangoDB default).
`dotnet build` → 0 errors.

```bash
git add SharpMUSH.Library/ SharpMUSH.Database/ SharpMUSH.Database.ArangoDB/ SharpMUSH.Database.SurrealDB/SurrealDatabase.ServerState.cs SharpMUSH.Database.Memgraph/MemgraphDatabase.ServerState.cs SharpMUSH.Tests/Database/SessionStoreDbTests.cs
git commit -m "feat: SharpSession model, ISharpDatabase session methods, ArangoDB impl + migration"
```

---

### Task 3: SurrealDB + Memgraph session stores

**Files:**
- Create: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Sessions.cs`
- Create: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Sessions.cs`
- Modify: remove Task 2 stubs from both `*.ServerState.cs`
- Modify: `SurrealDatabase.Migration.cs` (index define), `MemgraphDatabase.Migration.cs` (index)
- Test: `SharpMUSH.Tests/Database/SurrealSessionStoreTests.cs`

**Interfaces:** Consumes Task 2's `ISharpDatabase` session members + `SharpSession`. Produces the same behavior on both providers. Surreal table `session` (camelCase fields `accountId/expiryUnixMs/ttlMs/originIp`), record id `session:⟨token⟩`. Memgraph node `(:Session {token, accountId, expiryUnixMs, ttlMs, originIp})`.

- [ ] **Step 1: Write failing test** — `SurrealSessionStoreTests.cs`, mirror `SurrealServerStateTests.cs` (Task 2 of the previous feature — read it for the fresh-in-memory-SurrealDatabase construction helper) with the same two round-trip assertions as `SessionStoreDbTests`.

- [ ] **Step 2: Verify failure** — `NotImplementedException("Task 3")`.

- [ ] **Step 3: SurrealDB** (`SurrealDatabase.Sessions.cs`, camelCase record per the ServerState precedent):

```csharp
using SharpMUSH.Library.Models;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	internal class SessionDbRecord : Record
	{
		public string accountId { get; set; } = "";
		public long expiryUnixMs { get; set; }
		public long ttlMs { get; set; }
		public string originIp { get; set; } = "";
	}

	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
		=> await ExecuteAsync(
			"UPSERT type::thing('session', $token) SET accountId=$a, expiryUnixMs=$e, ttlMs=$t, originIp=$ip",
			new Dictionary<string, object?>
			{ ["token"] = session.Token, ["a"] = session.AccountId, ["e"] = session.ExpiryUnixMs, ["t"] = session.TtlMs, ["ip"] = session.OriginIp },
			cancellationToken);

	public async ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var r = await ExecuteAsync("SELECT * FROM type::thing('session', $token)",
			new Dictionary<string, object?> { ["token"] = token }, cancellationToken);
		var row = r.GetValue<List<SessionDbRecord>>(0)?.FirstOrDefault();
		return row is null ? null : new SharpSession
		{ Token = token, AccountId = row.accountId, ExpiryUnixMs = row.expiryUnixMs, TtlMs = row.ttlMs, OriginIp = row.originIp };
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
		=> await ExecuteAsync("DELETE type::thing('session', $token)",
			new Dictionary<string, object?> { ["token"] = token }, cancellationToken);

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> await ExecuteAsync("DELETE session WHERE accountId = $a",
			new Dictionary<string, object?> { ["a"] = accountId }, cancellationToken);

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
		=> await ExecuteAsync("DELETE session WHERE originIp = $ip",
			new Dictionary<string, object?> { ["ip"] = originIp }, cancellationToken);
}
```

Adjust `type::thing` / param binding to match the exact `ExecuteAsync` calling convention already used in `SurrealDatabase.Accounts.cs` (e.g. `StringRecordId` usage) — crib from that file. Add index defines in `SurrealDatabase.Migration.cs`'s `indexQueries`/DEFINE block: `DEFINE INDEX IF NOT EXISTS session_account ON session FIELDS accountId; DEFINE INDEX IF NOT EXISTS session_ip ON session FIELDS originIp;`.

- [ ] **Step 4: Memgraph** (`MemgraphDatabase.Sessions.cs`, camelCase, mirror `MemgraphDatabase.ServerState.cs` accessors):

```csharp
using Neo4j.Driver;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
		=> await ExecuteWithRetryAsync(
			"MERGE (s:Session {token: $token}) SET s.accountId=$a, s.expiryUnixMs=$e, s.ttlMs=$t, s.originIp=$ip",
			new { token = session.Token, a = session.AccountId, e = session.ExpiryUnixMs, t = session.TtlMs, ip = session.OriginIp },
			cancellationToken);

	public async ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var r = await ExecuteWithRetryAsync("MATCH (s:Session {token: $token}) RETURN s", new { token }, cancellationToken);
		if (r.Result.Count == 0) return null;
		var n = r.Result[0]["s"].As<INode>();
		return new SharpSession
		{
			Token = token,
			AccountId = n.Properties["accountId"].As<string>(),
			ExpiryUnixMs = n.Properties["expiryUnixMs"].As<long>(),
			TtlMs = n.Properties["ttlMs"].As<long>(),
			OriginIp = n.Properties["originIp"].As<string>()
		};
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
		=> await ExecuteWithRetryAsync("MATCH (s:Session {token: $token}) DELETE s", new { token }, cancellationToken);

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> await ExecuteWithRetryAsync("MATCH (s:Session {accountId: $a}) DELETE s", new { a = accountId }, cancellationToken);

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
		=> await ExecuteWithRetryAsync("MATCH (s:Session {originIp: $ip}) DELETE s", new { ip = originIp }, cancellationToken);
}
```

Add `CREATE INDEX ON :Session(token);` (and optionally `:Session(accountId)`, `:Session(originIp)`) to `MemgraphDatabase.Migration.cs`'s index block. Remove the Task 2 stubs from both `*.ServerState.cs`.

- [ ] **Step 5: Run + build + commit**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SurrealSessionStoreTests/*"` → PASS.
`dotnet build` → 0 errors (Memgraph compiles).

```bash
git add SharpMUSH.Database.SurrealDB/ SharpMUSH.Database.Memgraph/ SharpMUSH.Tests/Database/SurrealSessionStoreTests.cs
git commit -m "feat: SurrealDB and Memgraph session stores + indexes"
```

---

### Task 4: `DatabaseAccountSessionStore` — swap the store to DB-backed

**Files:**
- Create: `SharpMUSH.Library/Services/DatabaseAccountSessionStore.cs`
- Modify: `SharpMUSH.Server/Startup.cs:231` (swap registration)
- Test: `SharpMUSH.Tests.Integration/Auth/SessionPersistenceTests.cs`

**Interfaces:** Consumes `ISharpDatabase` session methods (Task 2/3), implements `IAccountSessionStore` (Task 1). Produces the DB-backed store registered as the singleton.

- [ ] **Step 1: Write failing integration test**

`SessionPersistenceTests.cs` (fixture per `AuthHttpControllerTests`): resolve `IAccountSessionStore` and `ISharpDatabase` from `factory.Services`; create a token, assert a NEW `DatabaseAccountSessionStore` instance (same DB) validates it (proves persistence, i.e. survives a "restart"); revoke-by-account and revoke-by-ip clear it.

```csharp
	[Test]
	public async Task Session_SurvivesNewStoreInstance_AndRevokes()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		var store = new DatabaseAccountSessionStore(db);
		var token = await store.CreateTokenAsync("node_accounts/1", TimeSpan.FromMinutes(15), "203.0.113.50");

		var fresh = new DatabaseAccountSessionStore(db); // simulates a server restart
		await Assert.That(await fresh.ValidateAsync(token)).IsEqualTo("node_accounts/1");

		await fresh.RevokeAllForIpAsync("203.0.113.50");
		await Assert.That(await fresh.ValidateAsync(token)).IsNull();
	}
```

- [ ] **Step 2: Verify failure** — `DatabaseAccountSessionStore` undefined.

- [ ] **Step 3: Implement**

```csharp
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// DB-backed account session store. Sessions persist across restarts and are revoked
/// (deleted) by token, account, or origin IP for immediate ban enforcement.
/// </summary>
public sealed class DatabaseAccountSessionStore(ISharpDatabase database) : IAccountSessionStore
{
	public async Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		await database.UpsertSessionAsync(new SharpSession
		{
			Token = token,
			AccountId = accountId,
			OriginIp = originIp,
			ExpiryUnixMs = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds(),
			TtlMs = (long)ttl.TotalMilliseconds
		}, ct);
		return token;
	}

	public async Task<string?> ValidateAsync(string token, CancellationToken ct = default)
	{
		var s = await database.GetSessionAsync(token, ct);
		if (s is null) return null;

		if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > s.ExpiryUnixMs)
		{
			await database.DeleteSessionAsync(token, ct);
			return null;
		}

		// Slide the rolling window.
		s.ExpiryUnixMs = DateTimeOffset.UtcNow.AddMilliseconds(s.TtlMs).ToUnixTimeMilliseconds();
		await database.UpsertSessionAsync(s, ct);
		return s.AccountId;
	}

	public Task RevokeAsync(string token, CancellationToken ct = default)
		=> database.DeleteSessionAsync(token, ct).AsTask();

	public Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)
		=> database.DeleteSessionsForAccountAsync(accountId, ct).AsTask();

	public Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default)
		=> database.DeleteSessionsForIpAsync(originIp, ct).AsTask();
}
```

- [ ] **Step 4: Swap DI** in `Startup.cs`:

```csharp
	services.AddSingleton<IAccountSessionStore, DatabaseAccountSessionStore>();
```

(replacing `InMemoryAccountSessionStore`). Keep `InMemoryAccountSessionStore` in the codebase — its unit tests still cover the in-memory semantics and it documents the interface.

- [ ] **Step 5: Run + regression + commit**

Run: `.../SessionPersistenceTests/*` → PASS. Regression: `AuthHttpControllerTests`, `MustChangePasswordTests`, `SetupFlowTests`, `AdminAccountsApiTests` → all PASS (they exercise the session path end-to-end).
`dotnet build` → 0 errors.

```bash
git add SharpMUSH.Library/Services/DatabaseAccountSessionStore.cs SharpMUSH.Server/Startup.cs SharpMUSH.Tests.Integration/Auth/SessionPersistenceTests.cs
git commit -m "feat: DB-backed account session store (survives restart; revoke by account/ip)"
```

---

# Phase 2 — Credential consolidation

### Task 5: `AccountSessionAuthenticationHandler`

**Files:**
- Create: `SharpMUSH.Server/Authentication/AccountSessionAuthenticationHandler.cs`
- Test: `SharpMUSH.Tests/Authentication/AccountSessionAuthHandlerTests.cs`

**Interfaces:**
- Consumes `IAccountSessionStore.ValidateAsync`, `IAccountService.GetByIdAsync`/`GetCharactersAsync`, `AccountClaimsService.ComputeAccountRoleAsync`/`ComputeGrantedScopesAsync`, `GameHub.CharacterDbrefClaim`, `PortalPermission.ClaimType`.
- Produces: scheme `AccountSessionAuthenticationHandler.SchemeName = "AccountSession"`. On a valid `Authorization: Bearer <token>` (or `?access_token=` query for websockets), authenticates with claims: `NameIdentifier`=accountId, `Name`=username, `ClaimTypes.Role`=derived account role, one `perm` claim per scope, and `character_dbref` for the account's primary/first character (if any). Rejects disabled accounts and MustChangePassword? — NO: MustChangePassword is enforced at the controller layer already; the handler only rejects invalid/expired tokens and disabled accounts (so a flagged account can still reach change-password). Returns `AuthenticateResult.NoResult()` when no token is present (lets other schemes/anonymous proceed).

- [ ] **Step 1: Write failing unit test** — construct the handler with NSubstitute doubles (mirror `AuthControllerDebugOttTests` construction style) and a fake `HttpContext` carrying an `Authorization` header; assert: valid token → `Succeeded` with role + `character_dbref` claims; unknown token → `Fail`; no header/query → `None` (`AuthenticateResult.None`). Use `AuthenticationHandler` test harness pattern: build via `UrlEncoder`, `IOptionsMonitor<AuthenticationSchemeOptions>`, `ILoggerFactory`, and set `Context` via `InitializeAsync`.

```csharp
	[Test]
	public async Task ValidToken_Authenticates_WithRoleAndDbrefClaims()
	{
		// arrange substitutes: sessionStore.ValidateAsync("good") -> "node_accounts/1";
		// accountService.GetByIdAsync -> non-disabled account; GetCharactersAsync -> [#1];
		// accountClaims -> Wizard + {"players.view"}.
		var handler = await CreateHandlerWithHeaderAsync("Bearer good");
		var result = await handler.AuthenticateAsync();
		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#1");
		await Assert.That(result.Principal!.IsInRole("Wizard")).IsTrue();
	}
```

(Provide the `CreateHandlerWithHeaderAsync` helper in the test file; it news up the handler, calls `InitializeAsync(scheme, context)` with a `DefaultHttpContext` whose `Request.Headers.Authorization` is set.)

- [ ] **Step 2: Verify failure** — type undefined.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Authenticates REST and SignalR requests bearing an account-session token, resolving
/// role/permission claims server-side (so bans/role changes take effect on the next request)
/// and emitting the <see cref="GameHub.CharacterDbrefClaim"/> the hub authorizes on.
/// </summary>
public class AccountSessionAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IAccountSessionStore sessionStore,
	IAccountService accountService,
	AccountClaimsService accountClaims)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "AccountSession";

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var token = ExtractToken();
		if (string.IsNullOrWhiteSpace(token))
			return AuthenticateResult.NoResult();

		var accountId = await sessionStore.ValidateAsync(token);
		if (accountId is null)
			return AuthenticateResult.Fail("Invalid or expired account session.");

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || account.IsDisabled)
			return AuthenticateResult.Fail("Account not found or disabled.");

		var role = await accountClaims.ComputeAccountRoleAsync(accountId);
		var scopes = await accountClaims.ComputeGrantedScopesAsync(accountId, role);

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, accountId),
			new(ClaimTypes.Name, account.Username),
			new(ClaimTypes.Role, role.ToString()),
		};
		claims.AddRange(scopes.Select(s => new Claim(PortalPermission.ClaimType, s)));

		var characters = await accountService.GetCharactersAsync(accountId);
		var primary = characters.FirstOrDefault();
		if (primary is not null)
			claims.Add(new Claim(GameHub.CharacterDbrefClaim, $"#{primary.Object.Key}"));

		var identity = new ClaimsIdentity(claims, SchemeName);
		return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
	}

	private string? ExtractToken()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return header["Bearer ".Length..].Trim();
		// SignalR WebSocket/SSE transports pass the token as a query parameter.
		return Request.Query["access_token"].FirstOrDefault();
	}
}
```

Note: the `character_dbref` is the account's *first* character. Task 8 introduces `switch-character` which will need the active-character concept; for now first-character is the connect default (matches how the client picks a character before connecting the hub).

- [ ] **Step 4: Run + commit**

Run: `.../AccountSessionAuthHandlerTests/*` → PASS. `dotnet build` → 0.

```bash
git add SharpMUSH.Server/Authentication/AccountSessionAuthenticationHandler.cs SharpMUSH.Tests/Authentication/AccountSessionAuthHandlerTests.cs
git commit -m "feat: AccountSession authentication handler (bearer + ?access_token, server-side claims)"
```

---

### Task 6: FusionCache the role/permission resolution

**Files:**
- Modify: `SharpMUSH.Server/Authentication/AccountClaimsService.cs`
- Create: `SharpMUSH.Server/Authentication/IAccountClaimsCache.cs` (invalidation seam) — OR add an `InvalidateAsync(accountId)` method to `AccountClaimsService`
- Test: `SharpMUSH.Tests/Authentication/AccountClaimsCacheTests.cs`

**Interfaces:**
- Consumes `IFusionCache` (constructor-inject, per `QueryCachingBehavior`).
- Produces: `ComputeAccountRoleAsync`/`ComputeGrantedScopesAsync` cache per account (key `account-claims:{accountId}`) with a short TTL (30s), and `AccountClaimsService.InvalidateAsync(string accountId)` → `ValueTask` that removes the cache entry. Later tasks (enforcement, disable) call `InvalidateAsync`.

- [ ] **Step 1: Write failing test** — two calls to `ComputeAccountRoleAsync` for the same account hit the underlying `IAccountService.GetCharactersAsync` once (NSubstitute `.Received(1)`); after `InvalidateAsync`, the next call hits it again.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement** — inject `IFusionCache cache` into `AccountClaimsService`'s primary constructor; wrap the bodies:

```csharp
	public async Task<PortalRole> ComputeAccountRoleAsync(string accountId, PortalRole activeRole, CancellationToken ct = default)
		=> await cache.GetOrSetAsync($"account-role:{accountId}:{activeRole}",
			async token => await ComputeAccountRoleCoreAsync(accountId, activeRole, token),
			options => options.Duration = TimeSpan.FromSeconds(30),
			token: ct);
```

Rename the existing body to `ComputeAccountRoleCoreAsync`; do the same wrapping for `ComputeGrantedScopesAsync` (key `account-scopes:{accountId}:{role}`). Add:

```csharp
	public async ValueTask InvalidateAsync(string accountId)
	{
		await cache.RemoveByTagAsync($"acct:{accountId}");
	}
```

and tag each `GetOrSetAsync` with `tags: [$"acct:{accountId}"]` so one invalidate clears both role and scope entries. (Match the `tags:` parameter usage from `QueryCachingBehavior`.)

- [ ] **Step 4: Run + commit**

Run: `.../AccountClaimsCacheTests/*` → PASS. Regression: `AuthHttpControllerTests` (role/permission still correct in login responses) → PASS.

```bash
git add SharpMUSH.Server/Authentication/AccountClaimsService.cs SharpMUSH.Tests/Authentication/AccountClaimsCacheTests.cs
git commit -m "feat: cache account role/permission resolution with tag-based invalidation"
```

---

### Task 7: Session-based character switching + `switch-character` endpoint

**Files:**
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (add `POST api/auth/switch-character`; note the account's active character)
- Test: `SharpMUSH.Tests.Integration/Auth/SwitchCharacterTests.cs`

**Interfaces:**
- Produces: `POST api/auth/switch-character` (account-session bearer) body `{ CharacterKey, CharacterCreationTime }` → validates the character belongs to the account, returns `{ ott }` (an OTT for that character to connect the terminal) — same shape/role as `GetMushToken`'s account-session branch. No new token family; the existing session stays. (This replaces `jwt-switch-character`.)

- [ ] **Step 1: Write failing test** — register account, create 2 characters; call `switch-character` with the second → 200 with a non-empty `ott`; wrong character (not linked) → 401; disabled account's session → 401.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement** — the logic is exactly `GetMushToken`'s account-session branch, extracted to an authenticated endpoint. Add to `AuthController`:

```csharp
	public record SwitchCharacterRequest(int CharacterKey, long CharacterCreationTime);
	public record SwitchCharacterResponse(string Ott, int ExpiresIn);

	[HttpPost("switch-character")]
	[Authorize(AuthenticationSchemes = AccountSessionAuthenticationHandler.SchemeName)]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> SwitchCharacter([FromBody] SwitchCharacterRequest request)
	{
		var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

		var characters = await accountService.GetCharactersAsync(accountId);
		var character = characters.FirstOrDefault(c =>
			c.Object.Key == request.CharacterKey && c.Object.CreationTime == request.CharacterCreationTime);
		if (character is null)
			return Unauthorized("Character is not linked to this account.");

		const int ttl = 60;
		var ott = await ottStore.CreateTokenAsync(new DBRef(character.Object.Key, character.Object.CreationTime), TimeSpan.FromSeconds(ttl));
		return Ok(new SwitchCharacterResponse(ott, ttl));
	}
```

Add `using System.Security.Claims;` and `using SharpMUSH.Server.Authentication;` if missing.

- [ ] **Step 4: Run + commit**

Run: `.../SwitchCharacterTests/*` → PASS.

```bash
git add SharpMUSH.Server/Controllers/AuthController.cs SharpMUSH.Tests.Integration/Auth/SwitchCharacterTests.cs
git commit -m "feat: session-based POST api/auth/switch-character (OTT for a linked character)"
```

---

### Task 8: Retire JWT; wire AccountSession as the default scheme

**Files:**
- Delete: `SharpMUSH.Server/Authentication/JwtService.cs`, `IJwtService.cs`, `JwtOptions.cs`, `JwtTokenResult.cs`, `JwtTokenResult`/`IJwtTokenResult` if present; `SharpMUSH.Library/Services/InMemoryRefreshTokenStore.cs`, `SharpMUSH.Library/Services/Interfaces/IRefreshTokenStore.cs`
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (remove `jwt-login`/`jwt-switch-character`/`jwt-refresh`, `JwtService` property, `RefreshCookieName`/`SetRefreshCookie`/`ClearRefreshCookie`, `JwtTokenResponse`/`JwtLoginRequest`/`JwtSwitchCharacterRequest`/`JwtRefreshRequest`)
- Modify: `SharpMUSH.Server/Startup.cs` (replace the three-case JWT block with AccountSession + DebugAuth + MushBasic wiring; remove `IRefreshTokenStore` and `IJwtService` registrations)
- Modify: `SharpMUSH.Server/Controllers/AccountController.cs`, `AdminAccountsController.cs`, and anywhere else that did ad-hoc `accountSessionStore.ValidateAsync` in a bearer helper — these can stay (they still work) OR switch to `[Authorize(AuthenticationSchemes = AccountSessionAuthenticationHandler.SchemeName)]` + `User.FindFirstValue(ClaimTypes.NameIdentifier)`. Keep the existing helper approach to minimize churn; just ensure `MustChangePassword`/disabled checks remain (they do).
- Modify: `SharpMUSH.Server/Controllers/AccountController.cs` Logout — drop the `RefreshCookieName` cookie deletion (cookie no longer exists).
- Test: `SharpMUSH.Tests.Integration/Auth/JwtRetirementTests.cs`

**Interfaces:** Consumes Tasks 5–7. Produces: no `jwt-*` endpoints (404); default auth scheme is `AccountSession` in production and `DebugAuth` in dev; `GameHub` authorizes under either. `Jwt:SigningKey` config no longer read.

- [ ] **Step 1: Write failing test**

```csharp
	[Test]
	public async Task JwtEndpoints_AreGone()
	{
		var http = CreateClient();
		var r1 = await http.PostAsJsonAsync("api/auth/jwt-login", new { UsernameOrEmail = "x", Password = "y", CharacterKey = 1, CharacterCreationTime = 0L });
		await Assert.That(r1.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		var r2 = await http.PostAsJsonAsync("api/auth/jwt-refresh", new { RefreshToken = "z" });
		await Assert.That(r2.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
```

- [ ] **Step 2: Verify failure** — currently these return 200/501, not 404.

- [ ] **Step 3: Delete + rewire.** Delete the JWT/refresh files. Strip the JWT members from `AuthController`. In `Startup.cs`, replace the entire three-case block (lines ~427–512) with:

```csharp
		// Single web credential: the account-session token. AccountSession is the default
		// scheme in production; DebugAuth remains the dev default (auto-admin). MushBasic
		// stays as the opt-in MCP scheme.
		var authBuilder = environment.IsDevelopment()
			? services.AddAuthentication(DebugAuthenticationHandler.SchemeName)
				.AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(
					DebugAuthenticationHandler.SchemeName, _ => { })
			: services.AddAuthentication(AccountSessionAuthenticationHandler.SchemeName);

		authBuilder.AddScheme<AuthenticationSchemeOptions, AccountSessionAuthenticationHandler>(
			AccountSessionAuthenticationHandler.SchemeName, _ => { });

		services.AddAuthentication()
			.AddScheme<AuthenticationSchemeOptions, MushBasicAuthenticationHandler>(
				MushBasicAuthenticationHandler.SchemeName, _ => { });
```

Remove `services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();` and any `IJwtService` registration. Remove the now-unused JWT `using`s (`JwtBearer`, `TokenValidationParameters`, etc.). `GameHub`'s `[Authorize]` now resolves against `AccountSession` (prod) / `DebugAuth` (dev) — both emit `character_dbref`.

Note: `GameHub`'s `[Authorize]` with no scheme uses the default. In dev that's DebugAuth (works). In prod that's AccountSession (works, via `?access_token=`). If tests run in dev and need to exercise AccountSession specifically, annotate a hub test to force the scheme — but the default-scheme path is the shipping behavior.

- [ ] **Step 4: Run + regression + commit**

Run: `.../JwtRetirementTests/*` → PASS. Regression (critical — broad): `AuthHttpControllerTests`, `MustChangePasswordTests`, `SetupFlowTests`, `AdminAccountsApiTests`, `LoginMatrixTests`, `SwitchCharacterTests`, `LoginsConfigApiTests`, `PlayerCreationApiTests` → all PASS. Any test referencing `JwtTokenResponse`/`jwt-*` must be updated or removed. `dotnet build` → 0.

```bash
git add -A SharpMUSH.Server/ SharpMUSH.Library/Services/ SharpMUSH.Tests.Integration/
git commit -m "feat!: retire JWT + refresh; AccountSession is the single web credential scheme"
```

---

### Task 9: Client — authenticate the hub via session token; drop JWT/refresh

**Files:**
- Modify: `SharpMUSH.Client/Services/AccountAuthService.cs` (remove JWT/refresh records + flows; character switch calls `switch-character`; the hub uses the session token)
- Modify: `SharpMUSH.Client/Services/GameHubConnectionFactory.cs` (token source is the account-session token) and its callers
- Modify: `SharpMUSH.Client/Layout/MainLayout.razor` `SwitchCharacterAsync` (uses `switch-character` → OTT → terminal; already background per the earlier fix)
- Test: `SharpMUSH.Tests.BUnit/Services/AccountAuthServiceHubTokenTests.cs` (or extend existing)

**Interfaces:** Consumes Task 7/8 endpoints. Produces: the client never mints/holds a JWT; the hub connection's `AccessTokenProvider` returns the account-session token; character switching calls `api/auth/switch-character`.

- [ ] **Step 1: Write failing/adjust test** — bUnit test asserting that when the hub connection is created, the token passed is the account-session token (via a fake `IGameHubConnectionFactory` capturing the arg), and that `AccountAuthService` has no `jwt`/`refresh` members. If the existing client has JWT plumbing, the test drives the switch-character path returning an OTT.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement** — in `AccountAuthService`, delete JWT/refresh records and any `jwt-*` calls; add a `SwitchCharacterAsync(character)` that POSTs `api/auth/switch-character` and returns the OTT; the hub factory `Create(accountSessionToken)` call sites pass `AccountSessionToken`. Since SignalR captures the token once at build time and the session token slides server-side (not rotated client-side), a reconnect re-builds with the current `AccountSessionToken` — confirm `WithAutomaticReconnect` re-invokes `AccessTokenProvider` (it does; the provider closure reads the field). Have `AccessTokenProvider` read `() => Task.FromResult<string?>(AccountAuth.AccountSessionToken)` rather than capturing a snapshot, so a refreshed session is picked up on reconnect.

- [ ] **Step 4: Run + commit**

Run: full `dotnet run --project SharpMUSH.Tests.BUnit` → 0 failed. `dotnet build` → 0.

```bash
git add SharpMUSH.Client/ SharpMUSH.Tests.BUnit/
git commit -m "feat: client authenticates SignalR + character switch via account session (no JWT)"
```

---

# Phase 3 — Enforcement core

### Task 10: GameHub connection registry + force-disconnect

**Files:**
- Create: `SharpMUSH.Server/Hubs/HubConnectionRegistry.cs` (singleton)
- Modify: `SharpMUSH.Server/Hubs/GameHub.cs` (register/deregister in `OnConnectedAsync`/`OnDisconnectedAsync`)
- Modify: `SharpMUSH.Server/Startup.cs` (register the singleton)
- Test: `SharpMUSH.Tests/Hubs/HubConnectionRegistryTests.cs`

**Interfaces:**
- Produces:
  - `HubConnectionRegistry` with `Add(string connectionId, string accountId, string originIp)`, `Remove(string connectionId)`, `ConnectionsForAccount(string accountId)` → `IReadOnlyList<string>`, `ConnectionsForIp(string originIp)` → `IReadOnlyList<string>`.
  - GameHub populates it from `Context.User` (`ClaimTypes.NameIdentifier`) and `Context.GetHttpContext()?.Connection.RemoteIpAddress`.

- [ ] **Step 1: Write failing test** — add two connections for account A (different ids), one for B; assert `ConnectionsForAccount("A")` returns both; `Remove` drops one; `ConnectionsForIp` filters correctly.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement** the registry (a `ConcurrentDictionary<string, (string AccountId, string Ip)>` keyed by connectionId with reverse scans, mirroring `InMemoryAccountSessionStore`'s `RevokeAllForIp` filter style). In `GameHub.OnConnectedAsync`, after resolving the dbref, also:

```csharp
		var accountId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
		if (accountId is not null)
			registry.Add(Context.ConnectionId, accountId, ip ?? "unknown");
```

Inject `HubConnectionRegistry registry` into the hub constructor. In `OnDisconnectedAsync`: `registry.Remove(Context.ConnectionId);`. Register `services.AddSingleton<HubConnectionRegistry>();`.

- [ ] **Step 4: Run + commit**

```bash
git add SharpMUSH.Server/Hubs/ SharpMUSH.Server/Startup.cs SharpMUSH.Tests/Hubs/HubConnectionRegistryTests.cs
git commit -m "feat: hub connection registry (account/ip -> connectionIds) for force-disconnect"
```

---

### Task 11: `BanEnforcementService`

**Files:**
- Create: `SharpMUSH.Server/Services/BanEnforcementService.cs`
- Modify: `SharpMUSH.Server/Startup.cs` (register)
- Test: `SharpMUSH.Tests.Integration/Auth/BanEnforcementTests.cs`

**Interfaces:**
- Consumes: `IAccountSessionStore.RevokeAllForAccountAsync`/`RevokeAllForIpAsync`, `AccountClaimsService.InvalidateAsync`, `IConnectionService.GetAll()`/`Get(DBRef)`, `IMessageBus.Publish(new DisconnectConnectionMessage(handle, reason))`, `HubConnectionRegistry`, `IHubContext<GameHub, IGameHubClient>` (for abort — via `HubLifetimeManager` or `Clients` + `Context.Abort`), `SitelockMatcher` (Task 14 — for host-rule IP matching; until Task 14, match exact IP string and glob).
- Produces:
  - `BanEnforcementService.EnforceAccountBanAsync(string accountId, CancellationToken ct = default)` → revoke sessions for account, invalidate claims cache, drop matching game handles (by bound player's account — resolved via connection metadata `AccountId`), abort SignalR connections for the account.
  - `BanEnforcementService.EnforceHostRuleAsync(string hostPattern, CancellationToken ct = default)` → for every live connection/session/hub connection whose IP or host matches `hostPattern`, revoke + disconnect + abort.

- [ ] **Step 1: Write failing integration test** (the enforcement matrix — the payoff test):

```csharp
	[Test]
	public async Task EnforceAccountBan_RevokesSession_AndPublishesDisconnect()
	{
		// register account + character; log in -> session token; (optionally) simulate a
		// live connection by registering a handle with the account's metadata.
		var (http, account) = await RegisterAccountAsync();
		var enforcement = factory.Services.GetRequiredService<BanEnforcementService>();
		var sessions = factory.Services.GetRequiredService<IAccountSessionStore>();

		await enforcement.EnforceAccountBanAsync(account.AccountId);

		// The session token no longer authenticates.
		await Assert.That(await sessions.ValidateAsync(account.AccountSessionToken)).IsNull();
		// (Assert DisconnectConnectionMessage published: use a captured IMessageBus test double,
		//  or assert via a registered handle that ConnectionService no longer lists it after the
		//  ConnectionServer consumer would have run — in the in-process test, assert the publish
		//  happened by substituting IMessageBus in the factory and checking Received.)
	}
```

For the publish assertion, the test overrides `IMessageBus` with an NSubstitute double in the test host (check `ServerTestWebApplicationBuilderFactory` for how a service is swapped for tests — the NotifyService is already substituted there) and asserts `.Received().Publish(Arg.Is<DisconnectConnectionMessage>(...))`.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement** — the service walks `connectionService.GetAll()` (an `IAsyncEnumerable`), matches by the connection's `Metadata["AccountId"]` (account ban) or `InternetProtocolAddress`/`HostName` (host rule), and publishes a `DisconnectConnectionMessage(handle, reason)` per match; calls `sessionStore.RevokeAllForAccountAsync`/`RevokeAllForIpAsync`; `accountClaims.InvalidateAsync(accountId)`; and for each `registry.ConnectionsForAccount(accountId)` / `ConnectionsForIp(ip)`, aborts via the hub lifetime manager:

```csharp
	// Abort a live SignalR connection.
	await hubContext.Clients.Client(connectionId).ReceiveOutput(/* optional "banned" notice */);
	// then abort — SignalR exposes abort via HubLifetimeManager or Context.Abort; use
	// IHubContext + the connection's abort:
	// (There is no direct IHubContext.Abort(connectionId); use a IHubContext<GameHub> +
	//  the HubConnectionContext store. Simplest supported path: track HubCallerContext in the
	//  registry at connect time and call Context.Abort() on it.)
```

**Design note for the implementer:** SignalR has no `IHubContext.Abort(connectionId)`. To force-abort, capture the `HubCallerContext` (or an `Action abort = () => Context.Abort()`) in `HubConnectionRegistry.Add` at connect time (extend Task 10's registry to store an `Action Abort`), and have `BanEnforcementService` invoke it. If Task 10 already shipped without it, add the `Action` overload here and update `GameHub` to pass `() => Context.Abort()`. Update the Task 10 registry test accordingly.

Register `services.AddSingleton<BanEnforcementService>();`.

- [ ] **Step 4: Run + commit**

```bash
git add SharpMUSH.Server/Services/BanEnforcementService.cs SharpMUSH.Server/Startup.cs SharpMUSH.Server/Hubs/ SharpMUSH.Tests.Integration/Auth/BanEnforcementTests.cs SharpMUSH.Tests/Hubs/
git commit -m "feat: BanEnforcementService — revoke sessions, drop game handles, abort SignalR"
```

---

### Task 12: Wire enforcement into disable + sitelock persist; `@boot` NATS fix

**Files:**
- Modify: `SharpMUSH.Library/Services/AccountService.cs` `DisableAccountAsync` — invoke enforcement. **Note:** `AccountService` is in `SharpMUSH.Library`; `BanEnforcementService` is in `SharpMUSH.Server`. Introduce an `IBanEnforcer` interface in `SharpMUSH.Library.Services.Interfaces` that `BanEnforcementService` implements, injected into `AccountService` (nullable/optional to avoid a hard Server dependency, or register a no-op in non-Server hosts).
- Modify: `SharpMUSH.Server/Controllers/SitelockController.cs` (`AddSitelockRule` → `EnforceHostRuleAsync`)
- Modify: `SharpMUSH.Implementation/Commands/WizardCommands.cs` `@BOOT` — publish `DisconnectConnectionMessage` alongside `Disconnect` (the bonus fix)
- Test: `SharpMUSH.Tests.Integration/Auth/BanEnforcementWiringTests.cs`; a socket test for `@boot`

**Interfaces:**
- Produces: `IBanEnforcer { ValueTask EnforceAccountBanAsync(string accountId, CancellationToken ct = default); ValueTask EnforceHostRuleAsync(string hostPattern, CancellationToken ct = default); }` implemented by `BanEnforcementService`. `DisableAccountAsync` calls `EnforceAccountBanAsync`; `SitelockController.AddSitelockRule` calls `EnforceHostRuleAsync` after persist+reload.

- [ ] **Step 1: Write failing tests** — (a) disabling an account with a live session revokes it (already partly true — assert the enforcement path, i.e. claims cache invalidated + hub abort attempted); (b) posting a sitelock rule matching a live connection publishes `DisconnectConnectionMessage`; (c) `@boot <player>` now publishes `DisconnectConnectionMessage` (socket test asserting the `IMessageBus` double received it).

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement.** Define `IBanEnforcer`; make `BanEnforcementService : IBanEnforcer`; register both the concrete and the interface (`services.AddSingleton<IBanEnforcer>(sp => sp.GetRequiredService<BanEnforcementService>());`). Inject `IBanEnforcer? banEnforcer = null` into `AccountService` (optional so `SharpMUSH.Library` tests without a Server host still construct it) and call `if (banEnforcer is not null) await banEnforcer.EnforceAccountBanAsync(accountId, ct);` inside `DisableAccountAsync` (keeping the existing `RevokeAllForAccountAsync` as a floor). In `SitelockController.AddSitelockRule`, after `configReloadService.SignalChange();`, resolve `IBanEnforcer` (inject it) and `await banEnforcer.EnforceHostRuleAsync(hostPattern);`. In `@BOOT`, replace `await ConnectionService!.Disconnect(handle);` with both the `Disconnect` and `await MessageBus!.Publish(new DisconnectConnectionMessage(handle, "BOOT"));` (mirror the QUIT path; confirm `MessageBus` static is available on the `Commands` partial as in `SocketCommands`).

- [ ] **Step 4: Run + commit**

```bash
git add SharpMUSH.Library/ SharpMUSH.Server/ SharpMUSH.Implementation/Commands/WizardCommands.cs SharpMUSH.Tests.Integration/ SharpMUSH.Tests/
git commit -m "feat: enforce bans on account disable + sitelock add; @boot closes the socket"
```

---

# Phase 4 — Sitelock matching, forwarded headers, mutation

### Task 13: `SitelockMatcher` (glob + CIDR + bare IP)

**Files:**
- Create: `SharpMUSH.Library/Services/SitelockMatcher.cs`
- Test: `SharpMUSH.Tests/Services/SitelockMatcherTests.cs`

**Interfaces:**
- Produces: `static class SitelockMatcher` with `bool Matches(string rulePattern, string ip, string host)` — true if the rule is a glob matching the host OR a CIDR/bare-IP matching the IP. Handles: `*.evil.com` (glob on host), `203.0.113.0/24` (CIDR on IP), `203.0.113.7` (exact IP), `192.168.*` (glob on IP string). IPv4 and IPv6 CIDR.

- [ ] **Step 1: Write failing tests** — table cases: `("*.evil.com","1.2.3.4","x.evil.com") => true`; `("203.0.113.0/24","203.0.113.7","h") => true`; `("203.0.113.0/24","203.0.114.7","h") => false`; `("203.0.113.7","203.0.113.7","h") => true`; `("10.*","10.9.9.9","h") => true`; `("*.good.com","1.2.3.4","x.evil.com") => false`.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement** — CIDR via `System.Net.IPNetwork` (`.NET 8+` `IPNetwork.Parse` + `.Contains(IPAddress)`), bare IP via `IPAddress.TryParse` equality, otherwise fall back to the existing glob logic (lift `WildcardMatch` from `WizardCommands.cs` into this shared helper and have `@SITELOCK` CHECK call `SitelockMatcher.Matches`). Test both host and IP against a glob rule (a glob rule matches if it matches either).

- [ ] **Step 4: Run + commit** — also update `WizardCommands.cs` CHECK to call `SitelockMatcher.Matches(rule.Key, hostToCheck, hostToCheck)` (host used for both when no IP context) and delete the private `WildcardMatch`.

```bash
git add SharpMUSH.Library/Services/SitelockMatcher.cs SharpMUSH.Implementation/Commands/WizardCommands.cs SharpMUSH.Tests/Services/SitelockMatcherTests.cs
git commit -m "feat: SitelockMatcher (glob + CIDR + bare IP), shared by check and enforcement"
```

Then retrofit `BanEnforcementService.EnforceHostRuleAsync` (Task 11) to use `SitelockMatcher.Matches` instead of the placeholder exact/glob match.

---

### Task 14: Forwarded headers

**Files:**
- Modify: `SharpMUSH.Server/Program.cs` (add `UseForwardedHeaders` as the first middleware in `ConfigureApp`)
- Modify: `SharpMUSH.Server/Startup.cs` (`services.Configure<ForwardedHeadersOptions>` with known networks/proxies from config)
- Modify: `SharpMUSH.Server/appsettings.json` / `deploy/docker-compose.prod.yml` env (a `ForwardedHeaders:KnownProxies`/`KnownNetworks` or a `SHARPMUSH_TRUSTED_PROXIES` list)
- Test: `SharpMUSH.Tests.Integration/ForwardedHeadersTests.cs`

**Interfaces:** Produces: behind a trusted proxy, `HttpContext.Connection.RemoteIpAddress` reflects `X-Forwarded-For`/`CF-Connecting-IP`; from an untrusted source the header is ignored (spoof-resistant).

- [ ] **Step 1: Write failing test** — a request with `X-Forwarded-For: 203.0.113.99` from an untrusted remote does NOT change the observed client IP (assert via an echo endpoint or the rate-limiter partition — add a tiny `GET api/debug/client-ip` dev-only endpoint that returns `HttpContext.Connection.RemoteIpAddress`, gated `IsDevelopment()`, for the test). With the loopback configured as a known proxy, the header IS honored.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement:**

```csharp
	// Startup.ConfigureServices
	services.Configure<ForwardedHeadersOptions>(opts =>
	{
		opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
		opts.ForwardedForHeaderName = "X-Forwarded-For"; // Cloudflare: also honor CF-Connecting-IP via a custom step if configured
		opts.KnownNetworks.Clear();
		opts.KnownProxies.Clear();
		foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
			if (System.Net.IPAddress.TryParse(proxy, out var ip)) opts.KnownProxies.Add(ip);
	});
```

```csharp
	// Program.ConfigureApp — FIRST, before UseRouting
	app.UseForwardedHeaders();
```

For Cloudflare's `CF-Connecting-IP`, add a small middleware that, when the remote is a known Cloudflare proxy, sets `HttpContext.Connection.RemoteIpAddress` from `CF-Connecting-IP` — implement only if `ForwardedHeaders:CloudflareMode` is true; otherwise `X-Forwarded-For` suffices (Caddy sets it).

- [ ] **Step 4: Run + commit**

```bash
git add SharpMUSH.Server/Program.cs SharpMUSH.Server/Startup.cs SharpMUSH.Server/appsettings.json deploy/docker-compose.prod.yml SharpMUSH.Tests.Integration/ForwardedHeadersTests.cs
git commit -m "feat: trusted forwarded-headers so web-side client IP is correct behind proxies"
```

---

### Task 15: Sitelock checks at auth surfaces

**Files:**
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (`AccountLogin`, `AccountRegister`, `GetMushToken`) — reject sitelocked hosts (`!connect`/`!create`)
- Modify: `SharpMUSH.Server/Controllers/SetupController.cs` (`Complete`) — `!create`
- Modify: `SharpMUSH.Implementation/Commands/SocketCommands.cs` (`Connect`, `HandleGuestLogin`) — `!connect`/`!guest` (telnet)
- Modify: `SharpMUSH.Server/Hubs/GameHub.cs` `OnConnectedAsync` — reject sitelocked host (`!connect`) by aborting the connection
- Create: `SharpMUSH.Server/Authentication/SitelockGuard.cs` (shared helper mapping flags→surfaces, reading options + resolving IP)
- Test: `SharpMUSH.Tests.Integration/Auth/SitelockCheckTests.cs`, socket test for telnet connect

**Interfaces:** Consumes `SitelockMatcher`, `IOptionsWrapper<SharpMUSHOptions>.CurrentValue.SitelockRules`. Produces `SitelockGuard.IsBlocked(ip, host, string surfaceFlag)` → `bool` (surfaceFlag ∈ `"!connect"`, `"!create"`, `"!guest"`).

- [ ] **Step 1: Write failing tests** — with a sitelock rule `{"203.0.113.0/24": ["!connect"]}` stubbed into the options substitute, `account-login` from that IP → 403; anonymous `GET /` still 200; register from a `!create` IP → 403; a non-matching IP logs in fine. (Use the config-mutation re-stub pattern from `LoginsConfigApiTests`.) For telnet, a socket test with a connection registered from a banned IP refuses `connect`.

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement** `SitelockGuard` (checks each rule via `SitelockMatcher.Matches(rule.Key, ip, host)` and whether the rule's flags contain the surface flag), and call it at each surface: REST returns `403 "Access from your location is restricted."`; telnet notifies + refuses; GameHub `OnConnectedAsync` aborts + returns before joining groups. Anonymous page GETs are never routed through the guard.

- [ ] **Step 4: Run + commit**

```bash
git add SharpMUSH.Server/ SharpMUSH.Implementation/Commands/SocketCommands.cs SharpMUSH.Tests.Integration/ SharpMUSH.Tests/
git commit -m "feat: sitelock gates auth surfaces (connect/create/guest); anonymous browsing stays open"
```

---

### Task 16: Implement `@sitelock` mutation + enforcement trigger

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/WizardCommands.cs` `@SITELOCK` (BAN/REGISTER/REMOVE + 2-arg add)
- Test: `SharpMUSH.Tests/Commands/SitelockCommandTests.cs`

**Interfaces:** Consumes `ISharpDatabase.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions)` + `ConfigurationReloadService.SignalChange()` (the `SitelockController` pattern) and `IBanEnforcer.EnforceHostRuleAsync`. The command persists rule changes and triggers enforcement on add.

- [ ] **Step 1: Write failing socket tests** — `@sitelock/ban *.evil.com` adds a rule with `!connect`/`!create`/`!guest` (assert it appears in `Configuration.CurrentValue.SitelockRules` after reload); `@sitelock/remove *.evil.com` removes it; `@sitelock/register <pat>` adds `!create register`. (The parser runs as #1 → passes `FLAG^WIZARD`. Config persistence goes through the real DB in the fixture.)

- [ ] **Step 2: Verify failure** — currently these return `NotImplemented`.

- [ ] **Step 3: Implement** — replace the `NotImplemented` bodies. BAN maps to `["!connect","!create","!guest"]`, REGISTER to `["!create","register"]`, the 2-arg add uses the provided flags, REMOVE deletes. Each mutation: build `updatedOptions = Configuration.CurrentValue with { SitelockRules = new SitelockRulesOptions(newRules) }`, `await Database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions)`, signal reload, and on add `await BanEnforcer.EnforceHostRuleAsync(pattern)`. Resolve the needed services from the `Commands` partial statics (add `Database`/`ConfigReloadService`/`BanEnforcer` statics if not present — mirror how `Configuration`/`Mediator` are exposed; check `Commands.cs`). Notify the wizard of success.

- [ ] **Step 4: Run + commit**

```bash
git add SharpMUSH.Implementation/Commands/WizardCommands.cs SharpMUSH.Tests/Commands/SitelockCommandTests.cs
git commit -m "feat: @sitelock ban/register/remove mutation persists rules and enforces immediately"
```

---

# Phase 5 — Docs & verification

### Task 17: Revise architectural decisions; full verification

**Files:**
- Modify: `docs/design/architectural-decisions.md` (§1.2 token strategy)
- Modify: `docs/todo/area-01-auth.md` (mark DB-backed stores + consolidation done)
- Modify: `CLAUDE.md` (Authentication Architecture section — JWT is gone)

- [ ] **Step 1: Revise `architectural-decisions.md` §1.2** — record the reversal: the account-session token is now the single web credential (DB-backed, revocable); JWT + refresh retired; rationale = single-instance deployment, revocation-heavy domain, JWT+hub path was never wired in production. Note that JWT may return audience-specifically for third-party API consumers if ever needed.

- [ ] **Step 2: Update `CLAUDE.md`** — the "Authentication Architecture" paragraph: replace the JWT/refresh description with "a single DB-backed account-session token authenticates REST and SignalR via the `AccountSession` scheme; roles/permissions resolve server-side (FusionCache); bans revoke sessions and drop live connections immediately."

- [ ] **Step 3: Full verification**

```bash
dotnet build                                     # 0 errors
dotnet run --project SharpMUSH.Tests             # all pass
dotnet run --project SharpMUSH.Tests.Integration # all pass (known cross-suite flake re-verified in isolation)
dotnet run --project SharpMUSH.Tests.BUnit       # all pass
```

- [ ] **Step 4: Commit**

```bash
git add docs/ CLAUDE.md
git commit -m "docs: record auth consolidation; JWT retired in favor of DB-backed sessions"
```

---

## Plan Self-Review Notes

- **Spec coverage:** DB-backed session store + origin IP (T1–4); `AccountSession` handler + SignalR query-token (T5, T8); server-side role/perm cache (T6); switch-character without JWT (T7); JWT/refresh retirement (T8); client hub via session token (T9); connection registry + force-disconnect (T10); `BanEnforcementService` three fan-outs (T11); enforcement wired to disable + sitelock + `@boot` fix (T12); `SitelockMatcher` glob/CIDR (T13); forwarded headers spoof-resistance (T14); auth-surface sitelock checks, anonymous browsing open (T15); `@sitelock` mutation (T16); architectural-decisions revision (T17). Every spec section maps to a task.
- **Adaptation points flagged inline** (the executor must match live code): SurrealDb `ExecuteAsync`/`StringRecordId` calling convention; Memgraph `.As<T>()` accessors; the `Commands` partial statics for `Database`/`ConfigReloadService`/`BanEnforcer`/`MessageBus`; the exact SignalR abort mechanism (capture `Context.Abort` in the registry — resolved in T10/T11); how `ServerTestWebApplicationBuilderFactory` substitutes a service for the `IMessageBus` publish assertion.
- **Ordering:** T1→T2→T3→T4 (store); T5–T7 depend on T4; T8 depends on T5–T7; T9 on T8; T10→T11→T12 (enforcement); T13 feeds T11's host matching and T15/T16; T14 before T15; T16 depends on T12's `IBanEnforcer`. T17 last.
- **Cross-project boundary:** `AccountService` (Library) reaching enforcement (Server) is solved via `IBanEnforcer` in Library with the impl in Server (T11/T12) — no Library→Server reference.
