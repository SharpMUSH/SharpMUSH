# Account / Login First-Run Setup & Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the env-var/default bootstrap admin with a first-run setup wizard gated on a MUSH-wide `SetupCompleted` flag, extend account login to accept character credentials, and harden the auth stack (debug-ott, MustChangePassword, PlayerCreation/Logins config, account disable, admin tooling).

**Architecture:** A new single-document `ServerState` (per-game) in all three DB providers carries `SetupCompleted`. `BootstrapService` pre-generates an *unclaimed* admin account (empty password hash) linked to God #1; the reworked `SetupController` claims it (first visitor wins). `AccountService.AuthenticateAsync` gains the full login matrix. Server-side enforcement is added for `MustChangePassword`, `Net.PlayerCreation`, `Net.Logins`; `debug-ott` is dev-gated; a wizard-locked `@account` command and an `/admin/accounts` portal page provide admin-driven password reset/disable.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor WASM (MudBlazor), ArangoDB/Memgraph/SurrealDB providers, source-generated Mediator, TUnit + Testcontainers (Podman), bUnit.

**Spec:** `docs/superpowers/specs/2026-07-13-account-login-setup-design.md`

## Global Constraints

- C# files: tabs, indent size 2. Razor files: spaces, indent 4. `TreatWarningsAsErrors` is on everywhere.
- Prefer `var`; no `this.`; `OneOf<T1,T2>` for service results (never nullable returns for error cases); source-generated `Mediator` (not MediatR).
- Test framework is **TUnit**. Run with `dotnet run --project <TestProject> -- --treenode-filter "/*/*/<Class>/<Method>"`. Testcontainers run via **Podman** (works on this machine — never claim containers are unavailable).
- Empty password hashes must NEVER match in account-level login. Telnet `connect` keeps its existing empty-hash special case (PennMUSH God first login) — do not touch it.
- The first admin account stays **pre-generated** by `BootstrapService`; the wizard claims it. Setup gating is **game state** (`ServerState.SetupCompleted`), not account state.
- Character password salt key format: `$"#{player.Object.Key}:{player.Object.CreationTime}"`. Account salt key: `$"account:{account.Id}:{account.CreatedAt}"`.
- Account IDs are normalized as `node_accounts/<key>` across ALL providers.
- Arango account docs use **PascalCase** fields; Surreal/Memgraph use **camelCase**. SurrealDb.Net matches record property names case-sensitively and ignores `[JsonPropertyName]`.
- Do not modify `deploy/docker-compose.prod.yml` semantics beyond removing the two bootstrap env lines (Task 17).
- New user-facing socket-command messages use plain `NotifyService.Notify(...)` strings (localization of the new messages is an accepted follow-up).

---

### Task 1: ServerState — model, interface, ArangoDB provider + migration

**Files:**
- Create: `SharpMUSH.Library/Models/SharpServerState.cs`
- Create: `SharpMUSH.Database.ArangoDB/ArangoDatabase.ServerState.cs`
- Create: `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddServerState.cs`
- Modify: `SharpMUSH.Library/ISharpDatabase.cs` (after the `#endregion` of Account Methods, ~line 804)
- Modify: `SharpMUSH.Database/DatabaseConstants.cs` (near `Layouts = "sys_layouts"`, line 43)
- Test: `SharpMUSH.Tests/Database/ServerStateTests.cs`

**Interfaces:**
- Consumes: existing `arangoDb`/`handle` fields on `ArangoDatabase` partial, `DatabaseConstants`, migration auto-discovery (`ArangoDatabase.Migration.cs:67` scans the assembly).
- Produces: `SharpServerState { bool SetupCompleted }`; `ISharpDatabase.GetServerStateAsync(CancellationToken)` → `ValueTask<SharpServerState>`; `ISharpDatabase.SetServerSetupCompletedAsync(bool, CancellationToken)` → `ValueTask`. Collection constant `DatabaseConstants.ServerState = "sys_server_state"`, fixed doc key `"state"`. All later tasks use these exact names.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Database/ServerStateTests.cs` (pattern: `SharpMUSH.Tests/Database/RoleRegistryTests.cs`):

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Tests.Infrastructure;
using TUnit.Core;

namespace SharpMUSH.Tests.Database;

public class ServerStateTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test, NotInParallel(nameof(ServerStateTests))]
	public async Task ServerState_RoundTrip()
	{
		var initial = await Db.GetServerStateAsync();
		// Fresh test DB: migration ran before any claimed accounts existed.
		await Assert.That(initial.SetupCompleted).IsFalse();

		await Db.SetServerSetupCompletedAsync(true);
		var after = await Db.GetServerStateAsync();
		await Assert.That(after.SetupCompleted).IsTrue();

		// Restore so later setup-flow tests see an unclaimed game.
		await Db.SetServerSetupCompletedAsync(false);
		var restored = await Db.GetServerStateAsync();
		await Assert.That(restored.SetupCompleted).IsFalse();
	}
}
```

Check the actual namespace used by `RoleRegistryTests.cs` for `ServerWebAppFactory` and `[ClassDataSource]` and mirror it exactly (drop the `using TUnit.Core;` line if that file doesn't have it).

- [ ] **Step 2: Run test to verify it fails to compile**

Run: `dotnet build SharpMUSH.Tests 2>&1 | tail -20`
Expected: compile errors — `SharpServerState` / `GetServerStateAsync` not defined.

- [ ] **Step 3: Create the model**

`SharpMUSH.Library/Models/SharpServerState.cs`:

```csharp
namespace SharpMUSH.Library.Models;

/// <summary>
/// Game-wide server state — a single document per game (fixed key), not per account.
/// </summary>
public class SharpServerState
{
	/// <summary>
	/// True once first-run setup has been completed (setup wizard, God's character
	/// password set, or @account/setupcomplete). While false, the web portal shows
	/// the first-run wizard.
	/// </summary>
	public bool SetupCompleted { get; set; }
}
```

- [ ] **Step 4: Extend the interface and constants**

In `SharpMUSH.Library/ISharpDatabase.cs`, immediately after the Account Methods `#endregion` (before the closing brace of the interface):

```csharp
	#region Server State Methods

	/// <summary>
	/// Returns the game-wide server state document. Returns a default
	/// (SetupCompleted = false) if the document does not exist yet.
	/// </summary>
	ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default);

	/// <summary>Sets the game-wide SetupCompleted flag (upserts the state document).</summary>
	ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default);

	#endregion
```

In `SharpMUSH.Database/DatabaseConstants.cs`, next to `Layouts`:

```csharp
	// Game-wide server state — a single document with fixed _key "state"
	// (SetupCompleted flag for the first-run wizard).
	public const string ServerState = "sys_server_state";
```

- [ ] **Step 5: Implement the ArangoDB provider partial**

`SharpMUSH.Database.ArangoDB/ArangoDatabase.ServerState.cs` (copy `using`s and class declaration line from `ArangoDatabase.Accounts.cs`):

```csharp
using System.Text.Json;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	private const string ServerStateDocKey = "state";

	public async ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR d IN @@c FILTER d._key == @key RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ServerState },
				{ "key", ServerStateDocKey }
			}, cancellationToken: cancellationToken);

		if (result.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } elem)
			return new SharpServerState();

		return new SharpServerState
		{
			SetupCompleted = elem.TryGetProperty("SetupCompleted", out var sc)
				&& sc.ValueKind == JsonValueKind.True
		};
	}

	public async ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ServerState },
				{ "key", ServerStateDocKey },
				{ "doc", new Dictionary<string, object?> { ["_key"] = ServerStateDocKey, ["SetupCompleted"] = value } }
			}, cancellationToken: cancellationToken);
	}
}
```

Match the exact `Query.ExecuteAsync` call shape used in `ArangoDatabase.Layouts.cs` (`UpsertLayoutAsync`) if the bindVars parameter name differs.

- [ ] **Step 6: Create the migration**

`SharpMUSH.Database.ArangoDB/Migrations/Migration_AddServerState.cs` — crib the exact `using`s, interface members, and collection-creation call shape from `Migration_AddAccounts.cs` (which is manually idempotent):

```csharp
using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the sys_server_state single-document collection and infers SetupCompleted
/// for existing deployments: a game that already has an account with a non-empty
/// password hash has been claimed and must not re-open the first-run wizard.
/// </summary>
public class Migration_AddServerState : IArangoMigration
{
	public long Id => 20260713_001; // highest existing is 20260614_002
	public string Name => "add_server_state";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		var collections = await migrator.Context.Collection.ListAsync(handle);
		if (collections.All(c => c.Name != DatabaseConstants.ServerState))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.ServerState,
				Type = ArangoCollectionType.Document
			});
		}

		// Upgrade inference: any claimed account (non-empty PasswordHash) => setup done.
		var claimed = await migrator.Context.Query.ExecuteAsync<bool>(handle,
			$"FOR a IN {DatabaseConstants.Accounts} FILTER a.PasswordHash != null AND a.PasswordHash != '' LIMIT 1 RETURN true");

		// INSERT-or-keep: never downgrade an existing flag if the migration re-runs.
		await migrator.Context.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc UPDATE {} IN @@c",
			new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ServerState },
				{ "key", "state" },
				{ "doc", new Dictionary<string, object?> { ["_key"] = "state", ["SetupCompleted"] = claimed.FirstOrDefault() } }
			});
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
```

If `Migration_AddAccounts.cs` checks collection existence via `Collection.ExistAsync` instead of `ListAsync`, use that form. The migration is auto-discovered — no registration needed.

Note: Memgraph and SurrealDB will fail to build here because they don't implement the new interface members yet — add **temporary** `NotImplementedException` stubs in `MemgraphDatabase.Accounts.cs` and `SurrealDatabase.Accounts.cs` (replaced for real in Task 2):

```csharp
	public ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException("Task 2");

	public ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException("Task 2");
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ServerStateTests/*"`
Expected: PASS (ArangoDB is the default test provider).

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Library/Models/SharpServerState.cs SharpMUSH.Library/ISharpDatabase.cs \
  SharpMUSH.Database/DatabaseConstants.cs SharpMUSH.Database.ArangoDB/ \
  SharpMUSH.Database.Memgraph/MemgraphDatabase.Accounts.cs SharpMUSH.Database.SurrealDB/SurrealDatabase.Accounts.cs \
  SharpMUSH.Tests/Database/ServerStateTests.cs
git commit -m "feat: add game-wide ServerState (SetupCompleted) — model, interface, ArangoDB"
```

---

### Task 2: ServerState — SurrealDB + Memgraph providers

**Files:**
- Create: `SharpMUSH.Database.SurrealDB/SurrealDatabase.ServerState.cs`
- Create: `SharpMUSH.Database.Memgraph/MemgraphDatabase.ServerState.cs`
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Accounts.cs` (remove Task 1 stubs)
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Accounts.cs` (remove Task 1 stubs)
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs` (setup inference)
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs` (setup inference)
- Test: `SharpMUSH.Tests/Database/SurrealServerStateTests.cs`

**Interfaces:**
- Consumes: `SharpServerState`, interface members from Task 1; Surreal `ExecuteAsync(sql, Dictionary<string,object?>, ct)` + `response.GetValue<List<T>>(0)`; Memgraph `ExecuteWithRetryAsync(cypher, anonParams, ct)`.
- Produces: same interface behavior on both providers. Surreal record id `server_state:state` (field `setupCompleted`); Memgraph node `(:ServerState {id: 'state', setupCompleted})`.

- [ ] **Step 1: Write the failing test**

`SharpMUSH.Tests/Database/SurrealServerStateTests.cs` — pattern on `SharpMUSH.Tests/Database/SurrealMigrationIdempotencyTests.cs` (read that file first and copy exactly how it constructs a **fresh in-memory SurrealDatabase** and runs `Migrate()`; reuse its helper/base if one exists):

```csharp
namespace SharpMUSH.Tests.Database;

public class SurrealServerStateTests
{
	// Construct the SurrealDatabase the same way SurrealMigrationIdempotencyTests does.

	[Test]
	public async Task FreshGame_SetupNotCompleted_RoundTrips()
	{
		var db = await CreateFreshMigratedSurrealDatabaseAsync(); // helper per idempotency-test pattern

		var initial = await db.GetServerStateAsync();
		await Assert.That(initial.SetupCompleted).IsFalse();

		await db.SetServerSetupCompletedAsync(true);
		await Assert.That((await db.GetServerStateAsync()).SetupCompleted).IsTrue();
	}

	[Test]
	public async Task ClaimedGame_MigrationInfersSetupCompleted()
	{
		var db = await CreateFreshMigratedSurrealDatabaseAsync();
		// Simulate a pre-upgrade deployment: claimed account exists, then migration re-runs.
		await db.CreateAccountAsync("upgrade-admin", null, "some-real-hash");
		await db.Migrate(); // idempotent; inference must flip the flag on re-run only if unset

		// A claimed DB whose state doc was created before the account will remain false —
		// the inference applies when the state doc is first created. Re-create scenario:
		var db2 = await CreateFreshSurrealDatabaseAsync(); // NOT yet migrated
		await SeedClaimedAccountRawAsync(db2);              // raw insert before first Migrate
		await db2.Migrate();
		await Assert.That((await db2.GetServerStateAsync()).SetupCompleted).IsTrue();
	}
}
```

Adapt helper names to what `SurrealMigrationIdempotencyTests` actually provides; if raw pre-migration seeding is impractical there, replace the second test with: migrate → `CreateAccountAsync` with non-empty hash → delete the `server_state:state` record via `db` raw query → `Migrate()` again → flag is true. The behavior under test: **inference only runs when the state document is missing, and a claimed account ⇒ true.**

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SurrealServerStateTests/*"`
Expected: FAIL — `NotImplementedException("Task 2")`.

- [ ] **Step 3: Implement SurrealDB**

`SharpMUSH.Database.SurrealDB/SurrealDatabase.ServerState.cs` (copy `using`s/class line from `SurrealDatabase.Accounts.cs`; note the camelCase-record serialization rule):

```csharp
using SharpMUSH.Library.Models;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	// SurrealDb.Net deserializes by exact (case-sensitive) field name; property names
	// must match the stored camelCase fields verbatim (same rule as AccountDbRecord).
	internal class ServerStateDbRecord : Record
	{
		public bool setupCompleted { get; set; }
	}

	public async ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM server_state:state",
			new Dictionary<string, object?>(), cancellationToken);
		var rows = response.GetValue<List<ServerStateDbRecord>>(0);
		return new SharpServerState { SetupCompleted = rows?.FirstOrDefault()?.setupCompleted ?? false };
	}

	public async ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
	{
		await ExecuteAsync("UPSERT server_state:state SET setupCompleted = $value",
			new Dictionary<string, object?> { ["value"] = value }, cancellationToken);
	}
}
```

Remove the Task 1 stubs from `SurrealDatabase.Accounts.cs`.

In `SurrealDatabase.Migration.cs`, inside `Migrate()` after the index/seed steps, add a call to a new private method and implement it:

```csharp
	// First-run setup inference: create the server_state doc if missing; a game that
	// already has a claimed account (non-empty passwordHash) must not re-open the wizard.
	private async Task EnsureServerStateAsync(CancellationToken cancellationToken)
	{
		var existing = await ExecuteAsync("SELECT * FROM server_state:state",
			new Dictionary<string, object?>(), cancellationToken);
		if (existing.GetValue<List<ServerStateDbRecord>>(0) is { Count: > 0 })
			return;

		var claimed = await ExecuteAsync(
			"SELECT id FROM account WHERE passwordHash != NONE AND passwordHash != '' LIMIT 1",
			new Dictionary<string, object?>(), cancellationToken);
		var setupCompleted = claimed.GetValue<List<AccountDbRecord>>(0) is { Count: > 0 };

		await ExecuteAsync("CREATE server_state:state CONTENT { setupCompleted: $value }",
			new Dictionary<string, object?> { ["value"] = setupCompleted }, cancellationToken);
	}
```

Match the `ct`/parameter plumbing style of the surrounding `Migrate()` code. If `AccountDbRecord` can't deserialize an id-only projection, use `SELECT * FROM account WHERE ... LIMIT 1` instead.

- [ ] **Step 4: Implement Memgraph**

`SharpMUSH.Database.Memgraph/MemgraphDatabase.ServerState.cs`:

```csharp
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	public async ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (s:ServerState {id: 'state'}) RETURN s.setupCompleted AS setupCompleted",
			new { }, cancellationToken);
		var record = result.Result.FirstOrDefault();
		return new SharpServerState
		{
			SetupCompleted = record is not null && record["setupCompleted"].As<bool?>() == true
		};
	}

	public async ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync(
			"MERGE (s:ServerState {id: 'state'}) SET s.setupCompleted = $value",
			new { value }, cancellationToken);
	}
}
```

Adapt the record-value access (`record["setupCompleted"].As<bool?>()`) to the exact accessor style used in `MemgraphDatabase.Accounts.cs` (`MapNodeToAccount`). Remove the Task 1 stubs.

In `MemgraphDatabase.Migration.cs`, inside `Migrate()` after seeding, add:

```csharp
		// First-run setup inference (only when the state node is missing): a game with a
		// claimed account (non-empty passwordHash) must not re-open the first-run wizard.
		var stateExists = (await ExecuteWithRetryAsync(
			"MATCH (s:ServerState {id: 'state'}) RETURN s LIMIT 1", new { }, cancellationToken)).Result.Any();
		if (!stateExists)
		{
			var claimed = (await ExecuteWithRetryAsync(
				"MATCH (a:Account) WHERE a.passwordHash IS NOT NULL AND a.passwordHash <> '' RETURN a LIMIT 1",
				new { }, cancellationToken)).Result.Any();
			await ExecuteWithRetryAsync(
				"CREATE (:ServerState {id: 'state', setupCompleted: $claimed})",
				new { claimed }, cancellationToken);
		}
```

Match the surrounding `Migrate()` code's cancellation-token variable name. Remember the Memgraph gotcha: `_migrated` is a process-wide static — the inference must live inside the guarded region that actually runs DDL.

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SurrealServerStateTests/*"`
Expected: PASS.
Run: `dotnet build` — Expected: 0 errors (Memgraph compiles).

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Database.SurrealDB/ SharpMUSH.Database.Memgraph/ SharpMUSH.Tests/Database/SurrealServerStateTests.cs
git commit -m "feat: ServerState for SurrealDB and Memgraph with upgrade inference"
```

---

### Task 3: Account DB extensions — disable flag, account listing, session revoke-all, AccountService admin methods

**Files:**
- Modify: `SharpMUSH.Library/ISharpDatabase.cs` (Account Methods region)
- Modify: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Accounts.cs`
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Accounts.cs`
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Accounts.cs`
- Modify: `SharpMUSH.Library/Services/Interfaces/IAccountSessionStore.cs`
- Modify: `SharpMUSH.Library/Services/InMemoryAccountSessionStore.cs`
- Modify: `SharpMUSH.Library/Services/Interfaces/IAccountService.cs`
- Modify: `SharpMUSH.Library/Services/AccountService.cs`
- Test: `SharpMUSH.Tests/Database/AccountAdminDbTests.cs`, extend `SharpMUSH.Tests/Services/InMemoryAccountSessionStoreTests.cs`

**Interfaces:**
- Consumes: provider account partials (Task 1 file layout), `InMemoryAccountSessionStore._tokens` (`ConcurrentDictionary<string, Entry(AccountId, Expiry, Ttl)>`).
- Produces (later tasks depend on these exact signatures):
  - `ISharpDatabase.UpdateAccountDisabledAsync(string accountId, bool value, CancellationToken ct = default)` → `ValueTask`
  - `ISharpDatabase.GetAllAccountsAsync(CancellationToken ct = default)` → `ValueTask<IReadOnlyList<SharpAccount>>`
  - `IAccountSessionStore.RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)` → `Task`
  - `IAccountService.SetPasswordAsync(string accountId, string newPassword, bool mustChangePassword, CancellationToken ct = default)` → `ValueTask<OneOf<Success, Error<string>>>`
  - `IAccountService.CreateUnclaimedAccountAsync(string username, CancellationToken ct = default)` → `ValueTask<SharpAccount>` (empty `PasswordHash`, bypasses hashing)
  - `IAccountService.DisableAccountAsync(string accountId, CancellationToken ct = default)` → real implementation (revokes sessions)
  - `IAccountService.EnableAccountAsync(string accountId, CancellationToken ct = default)` → `ValueTask<OneOf<Success, Error<string>>>`
  - `IAccountService.GetAllAccountsAsync(CancellationToken ct = default)` → `ValueTask<IReadOnlyList<SharpAccount>>`

- [ ] **Step 1: Write failing DB tests**

`SharpMUSH.Tests/Database/AccountAdminDbTests.cs` (same fixture pattern as `ServerStateTests`):

```csharp
	[Test, NotInParallel(nameof(AccountAdminDbTests))]
	public async Task DisableFlag_RoundTrip()
	{
		var account = await Db.CreateAccountAsync("disable-test-user", null, "hash-abc");
		await Db.UpdateAccountDisabledAsync(account.Id!, true);
		var reloaded = await Db.GetAccountByIdAsync(account.Id!);
		await Assert.That(reloaded!.IsDisabled).IsTrue();

		await Db.UpdateAccountDisabledAsync(account.Id!, false);
		reloaded = await Db.GetAccountByIdAsync(account.Id!);
		await Assert.That(reloaded!.IsDisabled).IsFalse();
	}

	[Test, NotInParallel(nameof(AccountAdminDbTests))]
	public async Task GetAllAccounts_IncludesCreated()
	{
		var account = await Db.CreateAccountAsync("list-test-user", null, "hash-def");
		var all = await Db.GetAllAccountsAsync();
		await Assert.That(all.Any(a => a.Id == account.Id)).IsTrue();
	}
```

Extend `SharpMUSH.Tests/Services/InMemoryAccountSessionStoreTests.cs`:

```csharp
	[Test]
	public async Task RevokeAllForAccount_RemovesOnlyThatAccountsTokens()
	{
		var store = new InMemoryAccountSessionStore();
		var t1 = await store.CreateTokenAsync("acct-A", TimeSpan.FromMinutes(15));
		var t2 = await store.CreateTokenAsync("acct-A", TimeSpan.FromMinutes(15));
		var t3 = await store.CreateTokenAsync("acct-B", TimeSpan.FromMinutes(15));

		await store.RevokeAllForAccountAsync("acct-A");

		await Assert.That(await store.ValidateAsync(t1)).IsNull();
		await Assert.That(await store.ValidateAsync(t2)).IsNull();
		await Assert.That(await store.ValidateAsync(t3)).IsEqualTo("acct-B");
	}
```

- [ ] **Step 2: Verify failure** — `dotnet build SharpMUSH.Tests 2>&1 | tail -5` → compile errors.

- [ ] **Step 3: Implement `ISharpDatabase` members in all three providers**

Interface (inside Account Methods region):

```csharp
	/// <summary>Sets or clears the account's disabled (banned) flag.</summary>
	ValueTask UpdateAccountDisabledAsync(string accountId, bool value, CancellationToken cancellationToken = default);

	/// <summary>Returns all accounts. Admin tooling only — account counts are small.</summary>
	ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default);
```

ArangoDB (`ArangoDatabase.Accounts.cs`, mirror `UpdateAccountPasswordAsync` / `GetAccountByUsernameAsync`):

```csharp
	public async ValueTask UpdateAccountDisabledAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(accountId);
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Accounts,
			new { _key = key, IsDisabled = value, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
			mergeObjects: true, cancellationToken: cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR a IN @@c SORT a.Username RETURN a",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Accounts } },
			cancellationToken: cancellationToken);
		return result.Where(e => e.ValueKind == JsonValueKind.Object).Select(AccountFromJson).ToList();
	}
```

SurrealDB (mirror the file's existing update/select methods, camelCase fields, `NormalizeSurrealId`/`AccountFieldSelection` helpers):

```csharp
	public async ValueTask UpdateAccountDisabledAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		await ExecuteAsync("UPDATE $accountId SET isDisabled = $value, updatedAt = $now",
			new Dictionary<string, object?>
			{
				["accountId"] = new StringRecordId(key),
				["value"] = value,
				["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}, cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync($"SELECT {AccountFieldSelection} FROM account ORDER BY username",
			new Dictionary<string, object?>(), cancellationToken);
		var rows = response.GetValue<List<AccountDbRecord>>(0) ?? [];
		return rows.Select(MapRecordToAccount).ToList();
	}
```

Memgraph (mirror existing update methods and `MapNodeToAccount`):

```csharp
	public async ValueTask UpdateAccountDisabledAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) SET a.isDisabled = $value, a.updatedAt = $now",
			new { id = key, value, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (a:Account) RETURN a ORDER BY a.username", new { }, cancellationToken);
		return result.Result.Select(r => MapNodeToAccount(r["a"].As<INode>())).ToList();
	}
```

- [ ] **Step 4: Session store revoke-all**

`IAccountSessionStore.cs`:

```csharp
	/// <summary>Invalidates every session token bound to the account (disable/ban).</summary>
	Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default);
```

`InMemoryAccountSessionStore.cs`:

```csharp
	public Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)
	{
		foreach (var pair in _tokens.Where(p => p.Value.AccountId == accountId))
			_tokens.TryRemove(pair.Key, out _);
		return Task.CompletedTask;
	}
```

- [ ] **Step 5: AccountService admin methods**

`IAccountService.cs` — add (and keep existing members):

```csharp
	/// <summary>Admin/setup password set: no old-password proof. Optionally flags MustChangePassword.</summary>
	ValueTask<OneOf<Success, Error<string>>> SetPasswordAsync(string accountId, string newPassword, bool mustChangePassword, CancellationToken ct = default);

	/// <summary>
	/// Creates an account with an EMPTY password hash (unclaimed — cannot be logged into
	/// until a password is set). Used by BootstrapService for the pre-generated admin.
	/// </summary>
	ValueTask<SharpAccount> CreateUnclaimedAccountAsync(string username, CancellationToken ct = default);

	ValueTask<OneOf<Success, Error<string>>> EnableAccountAsync(string accountId, CancellationToken ct = default);

	ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken ct = default);
```

`AccountService.cs` — change the constructor to `AccountService(ISharpDatabase database, IPasswordService passwordService, IAccountSessionStore accountSessionStore)` and implement:

```csharp
	public async ValueTask<OneOf<Success, Error<string>>> SetPasswordAsync(string accountId, string newPassword, bool mustChangePassword, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct);
		if (account is null)
			return new Error<string>("Account not found.");

		var newHash = passwordService.HashPassword(AccountKey(account), newPassword);
		await database.UpdateAccountPasswordAsync(accountId, newHash, ct);
		await database.UpdateAccountMustChangePasswordAsync(accountId, mustChangePassword, ct);
		return new Success();
	}

	public ValueTask<SharpAccount> CreateUnclaimedAccountAsync(string username, CancellationToken ct = default)
		=> database.CreateAccountAsync(username, null, string.Empty, ct);

	public async ValueTask<OneOf<Success, Error<string>>> EnableAccountAsync(string accountId, CancellationToken ct = default)
	{
		if (await database.GetAccountByIdAsync(accountId, ct) is null)
			return new Error<string>("Account not found.");
		await database.UpdateAccountDisabledAsync(accountId, false, ct);
		return new Success();
	}

	public ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken ct = default)
		=> database.GetAllAccountsAsync(ct);
```

Replace the `DisableAccountAsync` TODO stub body:

```csharp
	public async ValueTask<OneOf<Success, Error<string>>> DisableAccountAsync(string accountId, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct);
		if (account is null)
			return new Error<string>("Account not found.");

		await database.UpdateAccountDisabledAsync(accountId, true, ct);
		await accountSessionStore.RevokeAllForAccountAsync(accountId, ct);
		return new Success();
	}
```

Check DI: `AccountService` and `IAccountSessionStore` registrations in `SharpMUSH.Server/Startup.cs` (both should already be singletons; the new constructor parameter resolves automatically). Fix `SharpMUSH.Tests/Services/AccountServiceTests.cs` construction sites to pass an `InMemoryAccountSessionStore` (or NSubstitute).

- [ ] **Step 6: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountAdminDbTests/*"` → PASS
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/InMemoryAccountSessionStoreTests/*"` → PASS
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountServiceTests/*"` → PASS

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Library/ SharpMUSH.Database.ArangoDB/ SharpMUSH.Database.SurrealDB/ SharpMUSH.Database.Memgraph/ SharpMUSH.Tests/
git commit -m "feat: account disable, listing, session revoke-all, admin AccountService methods"
```

---

### Task 4: Login matrix in AccountService.AuthenticateAsync

**Files:**
- Modify: `SharpMUSH.Library/Services/AccountService.cs:13-26`
- Modify: `SharpMUSH.Library/Services/Interfaces/IAccountService.cs` (doc comment only)
- Test: `SharpMUSH.Tests.Integration/Auth/LoginMatrixTests.cs`

**Interfaces:**
- Consumes: `ISharpDatabase.GetPlayerByNameOrAliasAsync(string name, CancellationToken)` → `IAsyncEnumerable<SharpPlayer>` (ISharpDatabase.cs:482); `IPasswordService.PasswordIsValid/NeedsRehash/RehashPasswordAsync`; `DBRef(int, long?)`.
- Produces: `AuthenticateAsync` accepting — username/email + (account pw OR any linked character pw); character name + that character's pw → owning account. Empty hashes never match. Signature unchanged: `ValueTask<SharpAccount?> AuthenticateAsync(string usernameOrEmail, string password, CancellationToken ct = default)`.

- [ ] **Step 1: Write failing integration tests**

`SharpMUSH.Tests.Integration/Auth/LoginMatrixTests.cs` — copy the class scaffold (primary-ctor `ServerWebAppFactory` fixture, `CreateClient()` helper pinned to `https://localhost/`, `UniqueName`, `RegisterAccountAsync`, `CreateCharacterAsync` helpers and local DTO records) from `SharpMUSH.Tests.Integration/Auth/AuthHttpControllerTests.cs`, then add:

```csharp
	private const string AccountPassword = "account-pass-123";
	private const string CharPassword = "char-pass-456";

	[Test]
	public async Task Login_WithLinkedCharacterPassword_Succeeds()
	{
		var (http, account) = await RegisterAccountAsync(); // registers with AccountPassword
		var charName = UniqueName("MatrixChar");
		await CreateCharacterAsync(http, account.AccountSessionToken, charName, CharPassword);

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(account.Username, CharPassword));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task Login_WithCharacterNameAndPassword_ResolvesOwningAccount()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("MatrixChar");
		await CreateCharacterAsync(http, account.AccountSessionToken, charName, CharPassword);

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(charName, CharPassword));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var login = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(login!.Username).IsEqualTo(account.Username);
	}

	[Test]
	public async Task Login_CharacterName_WrongPassword_Fails()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("MatrixChar");
		await CreateCharacterAsync(http, account.AccountSessionToken, charName, CharPassword);

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(charName, AccountPassword)); // account pw is NOT valid via char-name identifier

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Login_EmptyHashAccount_NeverMatches()
	{
		// The bootstrap admin account has an empty hash; empty-string password must not open it.
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest("admin", ""));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest); // empty pw rejected by validation

		var response2 = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest("admin", "anything"));
		await Assert.That(response2.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}
```

Note: the `Login_EmptyHashAccount_NeverMatches` test depends on the bootstrap account being unclaimed — Task 6 changes bootstrap. Until Task 6 lands, that single test may fail in Development runs where `devpassword` bootstraps a hashed password; mark it `[Skip("Enabled by Task 6")]` now and un-skip it in Task 6.

- [ ] **Step 2: Verify failure**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/LoginMatrixTests/*"`
Expected: character-password and character-name tests FAIL (401 instead of 200).

- [ ] **Step 3: Implement the matrix**

Replace `AccountService.AuthenticateAsync` (lines 13-26) with:

```csharp
	public async ValueTask<SharpAccount?> AuthenticateAsync(string usernameOrEmail, string password, CancellationToken ct = default)
	{
		var account = usernameOrEmail.Contains('@')
			? await database.GetAccountByEmailAsync(usernameOrEmail, ct)
			: await database.GetAccountByUsernameAsync(usernameOrEmail, ct);

		// Character-like login: a character name resolves to its owning account,
		// authenticated by that character's password only.
		SharpPlayer? namedCharacter = null;
		if (account is null)
		{
			namedCharacter = await database.GetPlayerByNameOrAliasAsync(usernameOrEmail, ct)
				.FirstOrDefaultAsync(ct);
			if (namedCharacter is not null)
				account = await database.GetAccountForCharacterAsync(
					new DBRef(namedCharacter.Object.Key, namedCharacter.Object.CreationTime), ct);
		}

		if (account is null || account.IsDisabled)
			return null;

		// Empty stored hashes never match at the account level: God's PennMUSH-default empty
		// character password stays a telnet-connect special case, and the pre-generated
		// (unclaimed) admin account stays unlobbable until first-run setup claims it.
		if (!string.IsNullOrEmpty(account.PasswordHash)
			&& passwordService.PasswordIsValid(AccountKey(account), password, account.PasswordHash))
			return account;

		if (namedCharacter is not null)
			return await CharacterPasswordMatchesAsync(namedCharacter, password) ? account : null;

		var characters = await database.GetCharactersForAccountAsync(account.Id!, ct);
		foreach (var character in characters)
			if (await CharacterPasswordMatchesAsync(character, password))
				return account;

		return null;
	}

	private async ValueTask<bool> CharacterPasswordMatchesAsync(SharpPlayer character, string password)
	{
		if (string.IsNullOrEmpty(character.PasswordHash))
			return false;

		var key = $"#{character.Object.Key}:{character.Object.CreationTime}";
		if (!passwordService.PasswordIsValid(key, password, character.PasswordHash))
			return false;

		if (passwordService.NeedsRehash(character.PasswordHash))
			await passwordService.RehashPasswordAsync(character, password);

		return true;
	}
```

Add `using SharpMUSH.Library.Models;` if missing. Update the `IAccountService.AuthenticateAsync` doc comment to describe the matrix.

- [ ] **Step 4: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/LoginMatrixTests/*"` → PASS (except the skipped one)
Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/AuthHttpControllerTests/*"` → PASS (no regressions)
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountServiceTests/*"` → PASS (fix mock setups for `GetPlayerByNameOrAliasAsync` if they now need it — return an empty async enumerable by default)

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/ SharpMUSH.Tests.Integration/Auth/LoginMatrixTests.cs SharpMUSH.Tests/
git commit -m "feat: login matrix — character passwords and names as account credentials"
```

---

### Task 5: Mediator ServerState access + setup auto-complete on God password set

**Files:**
- Create: `SharpMUSH.Library/Queries/Database/GetServerStateQuery.cs`
- Create: `SharpMUSH.Library/Commands/Database/SetServerSetupCompletedCommand.cs`
- Create: `SharpMUSH.Implementation/Handlers/Database/GetServerStateQueryHandler.cs`
- Create: `SharpMUSH.Implementation/Handlers/Database/SetServerSetupCompletedCommandHandler.cs`
- Modify: `SharpMUSH.Implementation/Handlers/Database/SetPlayerPasswordCommandHandler.cs`
- Test: `SharpMUSH.Tests/Services/SetupAutoCompleteTests.cs`

**Interfaces:**
- Consumes: `ISharpDatabase.Get/SetServerSetupCompletedAsync` (Task 1), `SetPlayerPasswordCommand(SharpPlayer Player, string Password, string? Salt = null)`.
- Produces: `GetServerStateQuery() : IQuery<SharpServerState>`; `SetServerSetupCompletedCommand(bool Value) : ICommand<Unit>`. Setting #1's character password flips `SetupCompleted` (used by `@account/setupcomplete` in Task 12 and by the wizard escape hatch).

- [ ] **Step 1: Write the failing test**

`SharpMUSH.Tests/Services/SetupAutoCompleteTests.cs` (fixture pattern as in Task 1; get `IMediator` and `ISharpDatabase` from `WebAppFactoryArg.Services`):

```csharp
	[Test, NotInParallel("ServerStateTests")] // shares the ServerState doc with ServerStateTests
	public async Task SettingGodsPassword_CompletesSetup()
	{
		await Db.SetServerSetupCompletedAsync(false);

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var god = one.AsPlayer;
		await Mediator.Send(new SetPlayerPasswordCommand(god, "hashed-anything"));

		await Assert.That((await Db.GetServerStateAsync()).SetupCompleted).IsTrue();

		await Db.SetServerSetupCompletedAsync(false); // restore for setup-flow tests
	}

	[Test]
	public async Task ServerState_MediatorRoundTrip()
	{
		await Mediator.Send(new SetServerSetupCompletedCommand(false));
		var state = await Mediator.Send(new GetServerStateQuery());
		await Assert.That(state.SetupCompleted).IsFalse();
	}
```

Use the same `NotInParallel` group key as `ServerStateTests` (both mutate the singleton state doc).

- [ ] **Step 2: Verify failure** — build error (`GetServerStateQuery` undefined), and after stubs, the auto-complete assertion fails.

- [ ] **Step 3: Implement query/command + handlers**

`SharpMUSH.Library/Queries/Database/GetServerStateQuery.cs`:

```csharp
using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetServerStateQuery : IQuery<SharpServerState>;
```

`SharpMUSH.Library/Commands/Database/SetServerSetupCompletedCommand.cs`:

```csharp
using Mediator;

namespace SharpMUSH.Library.Commands.Database;

public record SetServerSetupCompletedCommand(bool Value) : ICommand<Unit>;
```

Handlers (mirror the header/style of `SetPlayerPasswordCommandHandler.cs`):

```csharp
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetServerStateQueryHandler(ISharpDatabase database) : IQueryHandler<GetServerStateQuery, SharpServerState>
{
	public async ValueTask<SharpServerState> Handle(GetServerStateQuery query, CancellationToken cancellationToken)
		=> await database.GetServerStateAsync(cancellationToken);
}
```

```csharp
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetServerSetupCompletedCommandHandler(ISharpDatabase database) : ICommandHandler<SetServerSetupCompletedCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetServerSetupCompletedCommand command, CancellationToken cancellationToken)
	{
		await database.SetServerSetupCompletedAsync(command.Value, cancellationToken);
		return new Unit();
	}
}
```

If the source-generated Mediator requires the `ValueTask<ValueTask<Unit>>` shape used by `SetPlayerPasswordCommandHandler` (`ICommand<ValueTask<Unit>>`), mirror that existing shape exactly instead.

- [ ] **Step 4: Add the auto-complete hook**

`SetPlayerPasswordCommandHandler.cs` becomes:

```csharp
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetPlayerPasswordCommandHandler(ISharpDatabase database) : ICommandHandler<SetPlayerPasswordCommand, ValueTask<Unit>>
{
	public async ValueTask<ValueTask<Unit>> Handle(SetPlayerPasswordCommand command, CancellationToken cancellationToken)
	{
		await database.SetPlayerPasswordAsync(command.Player, command.Password, command.Salt, cancellationToken);

		// Setting God's (#1) character password is the classic PennMUSH way of claiming a
		// fresh game (@password / @newpassword) — it also completes first-run setup so the
		// web wizard closes. (The transparent legacy-rehash path only runs after a valid
		// non-empty password check, so it cannot fire on an unclaimed game.)
		if (command.Player.Object.Key == 1)
		{
			var state = await database.GetServerStateAsync(cancellationToken);
			if (!state.SetupCompleted)
				await database.SetServerSetupCompletedAsync(true, cancellationToken);
		}

		return ValueTask.FromResult(new Unit());
	}
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SetupAutoCompleteTests/*"` → PASS

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Library/Queries/ SharpMUSH.Library/Commands/ SharpMUSH.Implementation/Handlers/ SharpMUSH.Tests/Services/SetupAutoCompleteTests.cs
git commit -m "feat: ServerState mediator access; God password set auto-completes setup"
```

---

### Task 6: Credential-free BootstrapService

**Files:**
- Modify: `SharpMUSH.Server/Services/BootstrapService.cs` (full rewrite)
- Delete: `SharpMUSH.Library/Models/BootstrapOptions.cs`
- Modify: `SharpMUSH.Server/Startup.cs:75-85` (remove `BootstrapOptions` Configure/PostConfigure block)
- Modify: `SharpMUSH.Server/appsettings.json` (remove `Bootstrap` section, lines 2-5)
- Modify: `SharpMUSH.Server/appsettings.Development.json` (remove `Bootstrap` section, lines 3-6)
- Test: `SharpMUSH.Tests.Integration/Auth/BootstrapTests.cs`; un-skip `LoginMatrixTests.Login_EmptyHashAccount_NeverMatches`

**Interfaces:**
- Consumes: `IAccountService.CreateUnclaimedAccountAsync` / `LinkCharacterAsync` / `HasAnyAccountAsync` (Task 3).
- Produces: on a fresh DB, exactly one account exists: username `admin`, `PasswordHash == ""`, linked to `#1`. No env vars, no generated password, no log banner. `SHARPMUSH_BOOTSTRAP_USERNAME`/`SHARPMUSH_BOOTSTRAP_PASSWORD` are dead (removed from docs in Task 17).

- [ ] **Step 1: Write failing test**

`SharpMUSH.Tests.Integration/Auth/BootstrapTests.cs` (fixture pattern as `AuthHttpControllerTests`; resolve `IAccountService` from `factory.Services`):

```csharp
	[Test]
	public async Task Bootstrap_PreGeneratesUnclaimedAdminLinkedToGod()
	{
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var admin = await accountService.GetAccountForCharacterAsync(new DBRef(1));

		await Assert.That(admin).IsNotNull();
		await Assert.That(admin!.PasswordHash).IsEqualTo(string.Empty);
	}
```

- [ ] **Step 2: Verify failure**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/BootstrapTests/*"`
Expected: FAIL — in the Development test host the current BootstrapService hashes `devpassword`, so `PasswordHash` is non-empty.

- [ ] **Step 3: Rewrite BootstrapService**

`SharpMUSH.Server/Services/BootstrapService.cs` full new content:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Runs once at startup. If no accounts exist, pre-generates the admin account linked
/// to player #1 (God) with an EMPTY password hash — unclaimed. It cannot be logged
/// into until first-run setup claims it (empty hashes never match in account login),
/// mirroring God's PennMUSH-default empty character password.
/// </summary>
public class BootstrapService(
	IAccountService accountService,
	ILogger<BootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (await accountService.HasAnyAccountAsync(cancellationToken))
		{
			logger.LogDebug("Bootstrap: accounts already exist, skipping.");
			return;
		}

		var account = await accountService.CreateUnclaimedAccountAsync("admin", cancellationToken);
		await accountService.LinkCharacterAsync(account.Id!, new DBRef(1), cancellationToken);

		logger.LogInformation(
			"Bootstrap: pre-generated unclaimed admin account linked to #1. " +
			"Complete first-run setup via the web portal (or set God's password in-game).");
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- [ ] **Step 4: Remove BootstrapOptions everywhere**

- Delete `SharpMUSH.Library/Models/BootstrapOptions.cs`.
- In `Startup.cs`, delete the `services.Configure<BootstrapOptions>(...)` + `services.PostConfigure<BootstrapOptions>(...)` block (lines 75-85).
- Remove the `"Bootstrap": { ... }` sections from both appsettings files.
- Grep for stragglers: `grep -rn "BootstrapOptions\|SHARPMUSH_BOOTSTRAP" --include='*.cs' .` — fix any test or DI references (docs/compose are Task 17).

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/BootstrapTests/*"` → PASS
Un-skip `LoginMatrixTests.Login_EmptyHashAccount_NeverMatches`; run `.../LoginMatrixTests/*` → PASS.
Run: `dotnet build` → 0 errors.
Check the Development debug flow still works: run `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/AuthHttpControllerTests/*"` → PASS (`debug-ott` returns the unclaimed account's session without needing a password).

- [ ] **Step 6: Commit**

```bash
git add -A SharpMUSH.Server/ SharpMUSH.Library/ SharpMUSH.Tests.Integration/
git commit -m "feat!: bootstrap pre-generates unclaimed admin — no env credentials, no banner"
```

---

### Task 7: SetupService + SetupController rework (the wizard claims the game)

**Files:**
- Create: `SharpMUSH.Server/Services/SetupService.cs`
- Modify: `SharpMUSH.Server/Controllers/SetupController.cs` (full rewrite)
- Modify: `SharpMUSH.Server/Startup.cs` (register `SetupService` singleton near the `BootstrapService` registration, ~line 245)
- Test: `SharpMUSH.Tests.Integration/Auth/SetupFlowTests.cs`

**Interfaces:**
- Consumes: `ISharpDatabase.Get/SetServerSetupCompletedAsync`, `IAccountService` (Tasks 1, 3), `DBRef(1)`.
- Produces: `SetupService.NeedsSetupAsync(ct)` → `ValueTask<bool>`; `SetupService.CompleteAsync(string username, string password, ct)` → `ValueTask<OneOf<Success, Error<string>>>`. HTTP: `GET api/setup/status` → `{ needsSetup: bool }`; `POST api/setup/complete { Username, Password }` → 200 / 400 (validation) / 409 (already complete or username taken). Client (Task 15) relies on these routes and shapes (already used by `AccountAuthService.NeedsSetupAsync/CompleteSetupAsync`).

- [ ] **Step 1: Write failing integration tests**

`SharpMUSH.Tests.Integration/Auth/SetupFlowTests.cs`:

```csharp
	// All setup tests share and mutate the single ServerState doc — strict ordering.
	private record SetupStatusResponse(bool NeedsSetup);
	private record SetupCompleteRequest(string Username, string Password);

	[Test, NotInParallel("SetupFlow", Order = 1)]
	public async Task Status_FreshGame_NeedsSetup()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);

		var http = CreateClient();
		var status = await http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
		await Assert.That(status!.NeedsSetup).IsTrue();
	}

	[Test, NotInParallel("SetupFlow", Order = 2)]
	public async Task Complete_ClaimsAdminAccount_AndLoginWorks()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/setup/complete",
			new SetupCompleteRequest("headwiz", "claimed-password-1"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var status = await http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
		await Assert.That(status!.NeedsSetup).IsFalse();

		var login = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest("headwiz", "claimed-password-1"));
		await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test, NotInParallel("SetupFlow", Order = 3)]
	public async Task Complete_SecondClaim_Returns409()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/setup/complete",
			new SetupCompleteRequest("sneaky", "other-password-2"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test, NotInParallel("SetupFlow", Order = 4)]
	public async Task Complete_ConcurrentClaims_ExactlyOneWins()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);

		var http = CreateClient();
		var first = http.PostAsJsonAsync("api/setup/complete", new SetupCompleteRequest("racer-one", "race-password-1"));
		var second = http.PostAsJsonAsync("api/setup/complete", new SetupCompleteRequest("racer-two", "race-password-2"));
		var responses = await Task.WhenAll(first, second);

		var statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();
		await Assert.That(statuses.Count(s => s == HttpStatusCode.OK)).IsEqualTo(1);
		await Assert.That(statuses.Count(s => s == HttpStatusCode.Conflict)).IsEqualTo(1);
	}

	[Test, NotInParallel("SetupFlow", Order = 5)]
	public async Task Complete_UsernameCollision_Returns409_WithoutConsumingClaim()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);
		var (_, existing) = await RegisterAccountAsync(); // takes a username

		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/setup/complete",
			new SetupCompleteRequest(existing.Username, "whatever-pass-3"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);

		var status = await http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
		await Assert.That(status!.NeedsSetup).IsTrue(); // claim not consumed

		await db.SetServerSetupCompletedAsync(true); // leave the shared game claimed for other suites
	}
```

Include the `AccountLoginRequest` record and helpers copied per `AuthHttpControllerTests`. Add `[Test, NotInParallel("SetupFlow", ...)]` ordering exactly as shown (TUnit `NotInParallel` with `Order` — check the attribute usage in the existing test suite; if `Order` isn't supported use `[DependsOn(nameof(...))]` chaining as `GuestLoginTests` does).

- [ ] **Step 2: Verify failure**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/SetupFlowTests/*"`
Expected: FAIL — current `SetupController` gates on `HasAnyAccountAsync` (bootstrap already created one → `NeedsSetup=false`, complete → 409 immediately).

- [ ] **Step 3: Implement SetupService**

`SharpMUSH.Server/Services/SetupService.cs`:

```csharp
using OneOf;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// First-run setup: while ServerState.SetupCompleted is false, the game is unclaimed and
/// the web wizard may claim it — first visitor wins. Claiming renames the pre-generated
/// #1-linked admin account, sets its password, and flips SetupCompleted.
/// </summary>
public class SetupService(ISharpDatabase database, IAccountService accountService)
{
	private readonly SemaphoreSlim _claimLock = new(1, 1);

	public async ValueTask<bool> NeedsSetupAsync(CancellationToken ct = default)
		=> !(await database.GetServerStateAsync(ct)).SetupCompleted;

	public async ValueTask<OneOf<Success, Error<string>>> CompleteAsync(string username, string password, CancellationToken ct = default)
	{
		await _claimLock.WaitAsync(ct);
		try
		{
			if ((await database.GetServerStateAsync(ct)).SetupCompleted)
				return new Error<string>("Setup has already been completed.");

			var account = await accountService.GetAccountForCharacterAsync(new DBRef(1), ct);
			if (account is null)
			{
				// Edge case: bootstrap never ran or the link was removed — create and link.
				account = await accountService.CreateUnclaimedAccountAsync(username, ct);
				await accountService.LinkCharacterAsync(account.Id!, new DBRef(1), ct);
			}
			else if (!string.Equals(account.Username, username, StringComparison.Ordinal))
			{
				var rename = await accountService.ChangeUsernameAsync(account.Id!, username, ct);
				if (rename.IsT1)
					return rename.AsT1; // username taken — claim NOT consumed
			}

			var setPassword = await accountService.SetPasswordAsync(account.Id!, password, mustChangePassword: false, ct);
			if (setPassword.IsT1)
				return setPassword.AsT1;

			await database.SetServerSetupCompletedAsync(true, ct);
			return new Success();
		}
		finally
		{
			_claimLock.Release();
		}
	}
}
```

Register in `Startup.cs` next to `AddHostedService<BootstrapService>()`:

```csharp
		services.AddSingleton<SetupService>();
```

- [ ] **Step 4: Rewrite SetupController**

`SharpMUSH.Server/Controllers/SetupController.cs` full new content:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// First-run setup endpoints, gated on the game-wide ServerState.SetupCompleted flag.
/// While setup is incomplete, the first visitor to complete the wizard claims the
/// pre-generated admin account (renames it and sets its password).
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(SetupService setupService) : ControllerBase
{
	public record SetupStatusResponse(bool NeedsSetup);
	public record SetupCompleteRequest(string Username, string Password);

	[HttpGet("status")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> GetStatus()
		=> Ok(new SetupStatusResponse(await setupService.NeedsSetupAsync()));

	[HttpPost("complete")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> Complete([FromBody] SetupCompleteRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and Password are required.");
		if (request.Password.Length < 8)
			return BadRequest("Password must be at least 8 characters.");

		var result = await setupService.CompleteAsync(request.Username.Trim(), request.Password);
		return result.Match<IActionResult>(
			_ => Ok(new { Message = "Setup complete. You can now log in." }),
			err => Conflict(err.Value));
	}
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/SetupFlowTests/*"` → PASS

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Server/ SharpMUSH.Tests.Integration/Auth/SetupFlowTests.cs
git commit -m "feat: first-run setup wizard claims the pre-generated admin (first visitor wins)"
```

---

### Task 8: debug-ott production gate

**Files:**
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (ctor + `GetDebugOtt`, line ~221)
- Test: `SharpMUSH.Tests/Controllers/AuthControllerDebugOttTests.cs`

**Interfaces:**
- Consumes: `Microsoft.Extensions.Hosting.IHostEnvironment` (`environment.IsDevelopment()` — the idiom used in `Startup.cs:450`).
- Produces: `GET api/auth/debug-ott` returns 404 in any non-Development environment, before any auth/DB work.

- [ ] **Step 1: Write the failing unit test**

`SharpMUSH.Tests/Controllers/AuthControllerDebugOttTests.cs` — construct the controller directly with NSubstitute doubles (check `AuthController`'s constructor parameter list and substitute each interface):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Controllers;

public class AuthControllerDebugOttTests
{
	private static AuthController CreateController(string environmentName)
	{
		var env = Substitute.For<IHostEnvironment>();
		env.EnvironmentName.Returns(environmentName);

		return new AuthController(
			Substitute.For<Mediator.IMediator>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IPasswordService>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IOttStore>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IAccountService>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IAccountSessionStore>(),
			Substitute.For<SharpMUSH.Library.Authorization.IRoleDerivationService>(),
			env,
			Substitute.For<Microsoft.Extensions.Logging.ILogger<AuthController>>());
	}

	[Test]
	public async Task DebugOtt_InProduction_Returns404()
	{
		var controller = CreateController(Environments.Production);
		var result = await controller.GetDebugOtt();
		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}
}
```

Match the constructor argument order to the actual `AuthController` primary constructor (currently: mediator, passwordService, ottStore, accountService, accountSessionStore, roleDerivation, logger — insert `environment` before `logger`).

- [ ] **Step 2: Verify failure** — compile error (no `IHostEnvironment` parameter).

- [ ] **Step 3: Implement**

In `AuthController`: add `IHostEnvironment environment` to the primary constructor (before `logger`), add `using Microsoft.Extensions.Hosting;`, and at the very top of `GetDebugOtt()`:

```csharp
		// Development-only. In production this endpoint must not exist even for
		// authenticated users — a valid player JWT must never mint a God OTT.
		if (!environment.IsDevelopment())
			return NotFound();
```

- [ ] **Step 4: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AuthControllerDebugOttTests/*"` → PASS
Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/AuthHttpControllerTests/*"` → PASS (test host is Development; behavior unchanged there)

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Server/Controllers/AuthController.cs SharpMUSH.Tests/Controllers/AuthControllerDebugOttTests.cs
git commit -m "fix(security): debug-ott returns 404 outside Development"
```

---

### Task 9: Server-side MustChangePassword enforcement

**Files:**
- Modify: `SharpMUSH.Server/Controllers/AccountController.cs` (replace `GetAccountIdFromBearerAsync` usage)
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (`GetMushToken` account-session path, `JwtSwitchCharacter`)
- Test: `SharpMUSH.Tests.Integration/Auth/MustChangePasswordTests.cs`

**Interfaces:**
- Consumes: `IAccountService.GetByIdAsync`, `SetPasswordAsync` (to flag a test account), session bearer pattern from `AccountController.cs:29-37`.
- Produces: while `MustChangePassword` is set, an account session token is accepted ONLY by `PUT api/account/password` and `POST api/account/logout`; all other `api/account/*` endpoints and the account-session paths of `mush-token`/`jwt-switch-character` return 403.

- [ ] **Step 1: Write failing tests**

`SharpMUSH.Tests.Integration/Auth/MustChangePasswordTests.cs` (scaffold per `AuthHttpControllerTests`):

```csharp
	[Test]
	public async Task FlaggedAccount_CannotListCharacters_ButCanChangePassword()
	{
		var (http, account) = await RegisterAccountAsync();
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.ForcePasswordChangeAsync(account.AccountId);

		// Blocked endpoint
		var listRequest = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		var listResponse = await http.SendAsync(listRequest);
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

		// Allowed endpoint
		var changeRequest = new HttpRequestMessage(HttpMethod.Put, "api/account/password")
		{
			Content = JsonContent.Create(new ChangePasswordRequest(Password, "brand-new-pass-1"))
		};
		changeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		var changeResponse = await http.SendAsync(changeRequest);
		await Assert.That(changeResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Unblocked afterwards
		var listAgain = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		listAgain.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		await Assert.That((await http.SendAsync(listAgain)).StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task FlaggedAccount_CannotMintOtt()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("McpChar");
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, charName, Password);
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.ForcePasswordChangeAsync(account.AccountId);

		var response = await http.PostAsJsonAsync("api/auth/mush-token",
			new MushTokenWithAccountRequest(account.AccountSessionToken, character.DbrefNumber, character.CreationTime));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
```

- [ ] **Step 2: Verify failure** — both blocked-endpoint assertions get 200 today.

- [ ] **Step 3: Implement in AccountController**

Replace the private helper with a variant carrying the failure result, and update every action:

```csharp
	/// <summary>
	/// Resolves the account session bearer. Unless <paramref name="allowMustChangePassword"/>,
	/// accounts flagged MustChangePassword are rejected with 403 — the flag is enforced
	/// server-side, not advisory: a flagged session may only change its password or log out.
	/// </summary>
	private async Task<(string? AccountId, IActionResult? Failure)> GetAccountIdFromBearerAsync(bool allowMustChangePassword = false)
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return (null, Unauthorized("Invalid or expired account session."));

		var token = header["Bearer ".Length..].Trim();
		var accountId = await accountSessionStore.ValidateAsync(token);
		if (accountId is null)
			return (null, Unauthorized("Invalid or expired account session."));

		if (!allowMustChangePassword)
		{
			var account = await accountService.GetByIdAsync(accountId);
			if (account is null || account.IsDisabled)
				return (null, Unauthorized("Account not found or disabled."));
			if (account.MustChangePassword)
				return (null, StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action."));
		}

		return (accountId, null);
	}
```

Update each action's prologue from:

```csharp
		var accountId = await GetAccountIdFromBearerAsync();
		if (accountId is null) return Unauthorized("Invalid or expired account session.");
```

to:

```csharp
		var (accountId, failure) = await GetAccountIdFromBearerAsync();
		if (failure is not null) return failure;
```

`ChangePassword` and `Logout` pass `allowMustChangePassword: true` (Logout reads the raw header itself today — leave its body as is, it never resolves the account).

- [ ] **Step 4: Implement in AuthController**

In `GetMushToken` (account-session branch, after `ValidateAsync` succeeds) and in `JwtSwitchCharacter` (after account load), add:

```csharp
			var sessionAccount = await accountService.GetByIdAsync(accountId);
			if (sessionAccount is null || sessionAccount.IsDisabled)
				return Unauthorized("Account not found or disabled.");
			if (sessionAccount.MustChangePassword)
				return StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action.");
```

(`JwtSwitchCharacter` already loads `account` — just add the `MustChangePassword` 403 branch after its existing disabled check.)

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/MustChangePasswordTests/*"` → PASS
Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/AuthHttpControllerTests/*"` → PASS

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Server/Controllers/ SharpMUSH.Tests.Integration/Auth/MustChangePasswordTests.cs
git commit -m "feat(security): enforce MustChangePassword server-side on session-token endpoints"
```

---

### Task 10: Enforce Net.PlayerCreation

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/AccountSocketCommands.cs` (`Register` ~line 21, `MakeCharacter` ~line 140)
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (`AccountRegister`)
- Modify: `SharpMUSH.Server/Controllers/AccountController.cs` (`CreateCharacter`)
- Test: `SharpMUSH.Tests/Commands/PlayerCreationConfigTests.cs`, `SharpMUSH.Tests.Integration/Auth/PlayerCreationApiTests.cs`

**Interfaces:**
- Consumes: `Configuration!.CurrentValue.Net.PlayerCreation` (socket commands static); `IOptionsWrapper<SharpMUSHOptions>` (AccountController already injects it as `options`; add the same to AuthController's ctor).
- Produces: `PlayerCreation = false` refuses telnet `register`/`make` (message: `"Player creation is disabled on this server."`) and web `account-register` / `POST api/account/characters` (403, same message).

- [ ] **Step 1: Write failing socket test**

`SharpMUSH.Tests/Commands/PlayerCreationConfigTests.cs` — pattern on `GuestLoginTests.cs` (fixture property, `Parser.CommandParse`, NSubstitute `NotifyService` assertions). The factory substitutes `IOptionsWrapper<SharpMUSHOptions>` with `.CurrentValue.Returns(config)` — temporarily re-stub and restore:

```csharp
	[Test, NotInParallel("ConfigMutation")]
	public async ValueTask Register_WhenPlayerCreationDisabled_Refuses()
	{
		var options = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		var restricted = original with { Net = original.Net with { PlayerCreation = false } };
		options.CurrentValue.Returns(restricted);
		try
		{
			var handle = 2001L;
			await Parser.CommandParse(handle, ConnectionService, MModule.single("register nocreate-user somepassword"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextEquals(s, "Player creation is disabled on this server.")),
				null, INotifyService.NotificationType.Announce);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
```

`SharpMUSHOptions`/`NetOptions` are records — `with` expressions work. Check `GuestLoginTests` for the exact `Notify` overload arguments (the trailing `null, INotifyService.NotificationType.Announce` may differ). Add the mirror test for `make` (log an account in first via `register` with creation enabled, then disable and try `make`).

`SharpMUSH.Tests.Integration/Auth/PlayerCreationApiTests.cs` — same `IOptionsWrapper` re-stub trick against `factory.Services`, then:

```csharp
	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountRegister_WhenDisabled_Returns403()
	{
		// stub PlayerCreation=false as above, try/finally restore
		var response = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(UniqueName("blocked"), null, "password-123"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task CreateCharacter_WhenDisabled_Returns403() { /* register while enabled, disable, POST api/account/characters, expect 403, restore */ }
```

- [ ] **Step 2: Verify failure** — commands/APIs succeed despite the flag.

- [ ] **Step 3: Implement**

`AccountSocketCommands.Register` — after the `LoggedIn` early-return:

```csharp
		if (!Configuration!.CurrentValue.Net.PlayerCreation)
		{
			await NotifyService!.Notify(handle, "Player creation is disabled on this server.");
			return new None();
		}
```

`AccountSocketCommands.MakeCharacter` — after the `AccountMode` check, same block.

`AuthController` — add `IOptionsWrapper<SharpMUSHOptions> options` to the ctor (`using SharpMUSH.Configuration.Options;` + the `IOptionsWrapper` namespace used by `AccountController.cs`), then at the top of `AccountRegister`:

```csharp
		if (!options.CurrentValue.Net.PlayerCreation)
			return StatusCode(StatusCodes.Status403Forbidden, "Player creation is disabled on this server.");
```

`AccountController.CreateCharacter` — same check right after the bearer resolution.

- [ ] **Step 4: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/PlayerCreationConfigTests/*"` → PASS
Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/PlayerCreationApiTests/*"` → PASS

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/ SharpMUSH.Server/ SharpMUSH.Tests/ SharpMUSH.Tests.Integration/
git commit -m "feat: enforce net player_creation on register/make and web registration"
```

---

### Task 11: Enforce Net.Logins

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/SocketCommands.cs` (`Connect` character path ~line 182-210, `HandleGuestLogin` ~line 291, `HandleTokenLogin` ~line 242)
- Modify: `SharpMUSH.Implementation/Commands/AccountSocketCommands.cs` (`Login`)
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (`AccountLogin`, `JwtLogin`, `GetMushToken` both paths)
- Test: `SharpMUSH.Tests/Commands/LoginsConfigTests.cs`, `SharpMUSH.Tests.Integration/Auth/LoginsConfigApiTests.cs`

**Interfaces:**
- Consumes: `Configuration!.CurrentValue.Net.Logins`; `AnySharpObject.IsWizard()` extension (as used at `GeneralCommands.cs:6207`); `IRoleDerivationService.DeriveRole` (AuthController already injects `roleDerivation`); `PortalRole` enum ordering.
- Produces: with `Logins = false` — telnet `connect`/`connect guest`/token login and web logins are refused for non-staff (message `"Logins are disabled."`, HTTP 403); staff = character #1 or any WIZARD-flagged character; accounts qualify when ANY linked character is staff. PennMUSH semantics: staff can always get in.

- [ ] **Step 1: Write failing tests**

`SharpMUSH.Tests/Commands/LoginsConfigTests.cs` (GuestLogin pattern + the Task 10 config-stub trick):

```csharp
	[Test, NotInParallel("ConfigMutation")]
	public async ValueTask Connect_WhenLoginsDisabled_NonStaffRefused_StaffAllowed()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		await Mediator.Send(new CreatePlayerCommand("LoginsPleb", "pleb-password-1", defaultHome, defaultHome, startingQuota));

		var options = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { Net = original.Net with { Logins = false } });
		try
		{
			var plebHandle = 3001L;
			await Parser.CommandParse(plebHandle, ConnectionService, MModule.single("connect LoginsPleb pleb-password-1"));
			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == plebHandle),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Logins are disabled.")),
				null, INotifyService.NotificationType.Announce);

			// Staff bypass: God (#1, empty hash) still connects with logins disabled.
			var godHandle = 3002L;
			var result = await Parser.CommandParse(godHandle, ConnectionService, MModule.single("connect God anything"));
			await Assert.That((result.Message?.ToString() ?? "").Contains("#-1")).IsFalse();

			// Guest login also refused.
			var guestHandle = 3003L;
			await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));
			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == guestHandle),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Logins are disabled.")),
				null, INotifyService.NotificationType.Announce);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
```

Copy the exact `Notify` overload arguments and any post-`CommandParse` settling delay from `GuestLoginTests`. God's handle must be registered with the connection service first if the factory requires it (mirror how `GuestLoginTests` uses fresh handles).

`SharpMUSH.Tests.Integration/Auth/LoginsConfigApiTests.cs`: register+character while enabled; disable; `account-login` → 403; restore.

```csharp
	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountLogin_WhenLoginsDisabled_NonStaff403()
	{
		var (http, account) = await RegisterAccountAsync();
		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Pleb"), Password);
		// stub Net.Logins=false (try/finally restore as Task 10)
		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(account.Username, Password));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
```

- [ ] **Step 2: Verify failure.**

- [ ] **Step 3: Implement socket-side**

`SocketCommands.Connect`, character branch — after password validation succeeds and before `Bind`:

```csharp
			if (!Configuration!.CurrentValue.Net.Logins
				&& foundDB.Object.Key != 1
				&& !await new AnySharpObject(foundDB).IsWizard())
			{
				await NotifyService!.Notify(handle, "Logins are disabled.");
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}
```

(Use the exact `AnySharpObject` construction already present in that method — `new Library.DiscriminatedUnions.AnySharpObject(foundDB)` if not `using`-aliased.)

`HandleGuestLogin` — at the top, alongside the `Net.Guests` check:

```csharp
		if (!Configuration!.CurrentValue.Net.Logins)
		{
			await NotifyService!.Notify(handle, "Logins are disabled.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
```

`HandleTokenLogin` — after resolving `foundPlayer`, same staff-exempt check as `Connect`.

`AccountSocketCommands.Login` — after `AuthenticateAsync` succeeds, before `BindAccount`:

```csharp
		if (!Configuration!.CurrentValue.Net.Logins)
		{
			var linked = await AccountService.GetCharactersAsync(account.Id!);
			if (!await AnyStaffCharacterAsync(linked))
			{
				await NotifyService!.Notify(handle, "Logins are disabled.");
				return new None();
			}
		}
```

with a private helper in the same partial class file:

```csharp
	private static async ValueTask<bool> AnyStaffCharacterAsync(IReadOnlyList<SharpPlayer> characters)
	{
		foreach (var character in characters)
		{
			if (character.Object.Key == 1)
				return true;
			if (await new AnySharpObject(character).IsWizard())
				return true;
		}
		return false;
	}
```

- [ ] **Step 4: Implement API-side**

In `AuthController`, add a private helper using the injected `roleDerivation`:

```csharp
	private async Task<bool> AnyStaffCharacterAsync(IReadOnlyList<SharpPlayer> characters)
	{
		foreach (var character in characters)
		{
			var flags = await character.Object.Flags.Value.ToListAsync();
			if (roleDerivation.DeriveRole(character.Object.Key, flags) >= PortalRole.Wizard)
				return true;
		}
		return false;
	}
```

In `AccountLogin` and `JwtLogin` — after loading `characters`:

```csharp
		if (!options.CurrentValue.Net.Logins && !await AnyStaffCharacterAsync(characters))
			return StatusCode(StatusCodes.Status403Forbidden, "Logins are disabled.");
```

In `GetMushToken`: account-session path — same check on the account's characters; character-credentials path — after password validation:

```csharp
		if (!options.CurrentValue.Net.Logins)
		{
			var flags = await player.Object.Flags.Value.ToListAsync();
			if (roleDerivation.DeriveRole(player.Object.Key, flags) < PortalRole.Wizard)
				return StatusCode(StatusCodes.Status403Forbidden, "Logins are disabled.");
		}
```

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LoginsConfigTests/*"` → PASS
Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/LoginsConfigApiTests/*"` → PASS
Regression: `.../GuestLoginTests/*` and `.../AuthHttpControllerTests/*` → PASS

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Implementation/ SharpMUSH.Server/ SharpMUSH.Tests/ SharpMUSH.Tests.Integration/
git commit -m "feat: enforce net logins — non-staff connections refused when disabled"
```

---

### Task 12: `@account` wizard command family

**Files:**
- Create: `SharpMUSH.Implementation/Commands/AccountAdminCommands.cs`
- Test: `SharpMUSH.Tests/Commands/AccountAdminCommandTests.cs`

**Interfaces:**
- Consumes: statics on `Commands` (`AccountService`, `Mediator`, `NotifyService`); `SetServerSetupCompletedCommand` (Task 5); `IAccountService.SetPasswordAsync/DisableAccountAsync/EnableAccountAsync/GetAllAccountsAsync/GetByUsernameAsync/GetCharactersAsync` (Task 3); `[SharpCommand]` pattern from `@PCREATE` (`WizardCommands.cs:1918`).
- Produces: `@account <name>`, `@account/list [pattern]`, `@account/newpassword <name>=<password>`, `@account/disable <name>`, `@account/enable <name>`, `@account/setupcomplete` — all `CommandLock = "FLAG^WIZARD"`.

- [ ] **Step 1: Write failing socket tests**

`SharpMUSH.Tests/Commands/AccountAdminCommandTests.cs` (GuestLoginTests pattern; the factory's parser runs as #1, which passes `FLAG^WIZARD`):

```csharp
	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountNewPassword_SetsPasswordAndFlag()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-reset-user", null, "old-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/newpassword cmd-reset-user=temp-password-9"));

		var authenticated = await accountService.AuthenticateAsync("cmd-reset-user", "temp-password-9");
		await Assert.That(authenticated).IsNotNull();
		await Assert.That(authenticated!.MustChangePassword).IsTrue();
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountDisable_BlocksLogin_EnableRestores()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-disable-user", null, "some-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/disable cmd-disable-user"));
		await Assert.That(await accountService.AuthenticateAsync("cmd-disable-user", "some-password-1")).IsNull();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/enable cmd-disable-user"));
		await Assert.That(await accountService.AuthenticateAsync("cmd-disable-user", "some-password-1")).IsNotNull();
	}

	[Test, NotInParallel("ServerStateTests")]
	public async ValueTask AccountSetupComplete_FlipsServerState()
	{
		var db = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/setupcomplete"));

		await Assert.That((await db.GetServerStateAsync()).SetupCompleted).IsTrue();
	}
```

Note: `CreateAccountAsync("...", null, "old-password-1")` on `IAccountService` hashes properly (two-phase). Wait for command effects if the queue is async — copy any `Task.Delay(200)` settling used by `GuestLoginTests` after `CommandParse`.

- [ ] **Step 2: Verify failure** — unknown command `@ACCOUNT` (huffman/parse error notification or no effect).

- [ ] **Step 3: Implement the command**

`SharpMUSH.Implementation/Commands/AccountAdminCommands.cs`:

```csharp
using OneOf.Types;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Wizard-only web-account administration.
	/// <para>Syntax:
	/// <c>@account &lt;name&gt;</c> — show account details;
	/// <c>@account/list [pattern]</c>;
	/// <c>@account/newpassword &lt;name&gt;=&lt;password&gt;</c> — set + force change on next login;
	/// <c>@account/disable &lt;name&gt;</c> / <c>@account/enable &lt;name&gt;</c>;
	/// <c>@account/setupcomplete</c> — mark first-run setup done (closes the web wizard).</para>
	/// </summary>
	[SharpCommand(Name = "@ACCOUNT", Switches = ["LIST", "NEWPASSWORD", "DISABLE", "ENABLE", "SETUPCOMPLETE"],
		Behavior = CommandBehavior.Default | CommandBehavior.EqSplit | CommandBehavior.RSNoParse,
		CommandLock = "FLAG^WIZARD", MinArgs = 0, MaxArgs = 2, ParameterNames = ["name", "password"])]
	public static async ValueTask<Option<CallState>> AccountAdmin(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.ToArray();
		var args = parser.CurrentState.Arguments;
		var arg0 = args.TryGetValue("0", out var a0) ? a0.Message?.ToPlainText()?.Trim() : null;
		var arg1 = args.TryGetValue("1", out var a1) ? a1.Message?.ToPlainText()?.Trim() : null;

		if (switches.Contains("SETUPCOMPLETE"))
		{
			await Mediator!.Send(new SetServerSetupCompletedCommand(true));
			await NotifyService!.Notify(executor, "First-run setup marked complete. The web setup wizard is closed.");
			return CallState.Empty;
		}

		if (switches.Contains("LIST"))
		{
			var accounts = await AccountService!.GetAllAccountsAsync();
			var filtered = string.IsNullOrWhiteSpace(arg0)
				? accounts
				: accounts.Where(a => a.Username.Contains(arg0, StringComparison.OrdinalIgnoreCase)).ToList();
			var lines = filtered.Select(a =>
				$"{a.Username,-30} {(a.IsDisabled ? "DISABLED" : "active"),-10} {(a.MustChangePassword ? "must-change-pw" : string.Empty)}");
			await NotifyService!.Notify(executor,
				filtered.Count == 0 ? "No matching accounts." : string.Join("\n", lines));
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(arg0))
		{
			await NotifyService!.Notify(executor, "Usage: @account[/list|/newpassword|/disable|/enable] <name>[=<password>]");
			return CallState.Empty;
		}

		var account = await AccountService!.GetByUsernameAsync(arg0);
		if (account is null)
		{
			await NotifyService!.Notify(executor, $"No account named '{arg0}'.");
			return CallState.Empty;
		}

		if (switches.Contains("NEWPASSWORD"))
		{
			if (string.IsNullOrWhiteSpace(arg1))
			{
				await NotifyService!.Notify(executor, "Usage: @account/newpassword <name>=<password>");
				return CallState.Empty;
			}

			var result = await AccountService.SetPasswordAsync(account.Id!, arg1, mustChangePassword: true);
			await NotifyService!.Notify(executor, result.Match(
				_ => $"Password for account '{account.Username}' set. They must change it at next login.",
				err => err.Value));
			return CallState.Empty;
		}

		if (switches.Contains("DISABLE"))
		{
			var result = await AccountService.DisableAccountAsync(account.Id!);
			await NotifyService!.Notify(executor, result.Match(
				_ => $"Account '{account.Username}' disabled; active sessions revoked.",
				err => err.Value));
			return CallState.Empty;
		}

		if (switches.Contains("ENABLE"))
		{
			var result = await AccountService.EnableAccountAsync(account.Id!);
			await NotifyService!.Notify(executor, result.Match(
				_ => $"Account '{account.Username}' enabled.",
				err => err.Value));
			return CallState.Empty;
		}

		// No switch: show details.
		var characters = await AccountService.GetCharactersAsync(account.Id!);
		var charList = characters.Count == 0
			? "  (none)"
			: string.Join("\n", characters.Select(c => $"  {c.Object.Name} (#{c.Object.Key})"));
		await NotifyService!.Notify(executor,
			$"Account: {account.Username}\n" +
			$"Email: {account.Email ?? "(none)"}\n" +
			$"Status: {(account.IsDisabled ? "DISABLED" : "active")}{(account.MustChangePassword ? ", must change password" : string.Empty)}\n" +
			$"Characters:\n{charList}");
		return CallState.Empty;
	}
}
```

Adjust `using`s / `Notify(executor, ...)` overload (an `AnySharpObject` executor is accepted — see `@PURGE`'s `NotifyLocalized(executor, ...)`; the plain `Notify` has an equivalent overload; if not, use `executor.Object().DBRef`).

- [ ] **Step 4: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountAdminCommandTests/*"` → PASS

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Commands/AccountAdminCommands.cs SharpMUSH.Tests/Commands/AccountAdminCommandTests.cs
git commit -m "feat: @account wizard command — list, details, newpassword, disable/enable, setupcomplete"
```

---

### Task 13: Account role/permissions in login response + admin accounts API

**Files:**
- Create: `SharpMUSH.Server/Authentication/AccountClaimsService.cs`
- Create: `SharpMUSH.Server/Controllers/AdminAccountsController.cs`
- Modify: `SharpMUSH.Server/Authentication/JwtService.cs` (extract private helpers)
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs` (`AccountLoginResponse` + both issuers)
- Modify: `SharpMUSH.Server/Startup.cs` (register `AccountClaimsService`)
- Test: `SharpMUSH.Tests.Integration/Auth/AdminAccountsApiTests.cs`

**Interfaces:**
- Consumes: `JwtService.ComputeAccountRoleAsync` (JwtService.cs:113-133) and `ComputeGrantedScopesAsync` (JwtService.cs:142-155) — move these two private methods verbatim into the new service; `IRoleDerivationService`, `IPermissionResolver` (whatever the moved bodies already inject); Task 3/9 service methods.
- Produces:
  - `AccountClaimsService.ComputeAccountRoleAsync(string accountId, CancellationToken)` → `Task<PortalRole>`; `ComputeGrantedScopesAsync(...)` with the moved signature. `JwtService` delegates to it (no behavior change).
  - `AccountLoginResponse` gains `string Role` and `IReadOnlyList<string> Permissions` (login + register + debug-ott account payloads).
  - `api/admin/accounts` (account-session bearer, requires account role ≥ Wizard, MustChangePassword-clean):
    - `GET api/admin/accounts?search=` → `[{ Id (key only), Username, Email, IsDisabled, MustChangePassword, Characters: [{DbrefNumber, Name}] }]`
    - `POST api/admin/accounts/{key}/reset-password { NewPassword }` → 204 (sets `MustChangePassword`)
    - `POST api/admin/accounts/{key}/disable` → 204; `POST api/admin/accounts/{key}/enable` → 204
    - `DELETE api/admin/accounts/{key}/characters/{dbrefNumber}` → 204
  - Route `{key}` is the id suffix; controller reconstructs `node_accounts/{key}`.

- [ ] **Step 1: Write failing tests**

`SharpMUSH.Tests.Integration/Auth/AdminAccountsApiTests.cs` (scaffold per `AuthHttpControllerTests`). Getting a Wizard account: register an account, create a character, then set the WIZARD flag on it via the parser (`WebAppFactoryArg`-equivalent is `factory.CommandParserFor` — or simpler, link the account to a character and flag it with `@set <name>=WIZARD` through `factory.CommandParser`). Simplest robust route: use the God-linked bootstrap admin — claim it via `api/setup/complete` if unclaimed, then `account-login`:

```csharp
	private record AdminAccountRow(string Id, string Username, string? Email, bool IsDisabled, bool MustChangePassword);

	private async Task<(HttpClient Http, string SessionToken)> LoginAsGodAccountAsync()
	{
		var http = CreateClient();
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var admin = await accountService.GetAccountForCharacterAsync(new DBRef(1));
		if (string.IsNullOrEmpty(admin!.PasswordHash))
			await accountService.SetPasswordAsync(admin.Id!, "god-admin-pass-1", mustChangePassword: false);
		await db.SetServerSetupCompletedAsync(true);

		var login = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(admin.Username, "god-admin-pass-1"));
		var body = await login.Content.ReadFromJsonAsync<AccountLoginResponse>();
		return (http, body!.AccountSessionToken);
	}

	[Test, NotInParallel("AdminAccounts")]
	public async Task List_RequiresWizardRole()
	{
		var (http, account) = await RegisterAccountAsync(); // plain player account
		var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/accounts");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		await Assert.That((await http.SendAsync(request)).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test, NotInParallel("AdminAccounts")]
	public async Task GodAccount_CanListAndResetPassword()
	{
		var (http, sessionToken) = await LoginAsGodAccountAsync();
		var (_, target) = await RegisterAccountAsync();

		var listRequest = new HttpRequestMessage(HttpMethod.Get, "api/admin/accounts");
		listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		var listResponse = await http.SendAsync(listRequest);
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var rows = await listResponse.Content.ReadFromJsonAsync<List<AdminAccountRow>>();
		var targetRow = rows!.Single(r => r.Username == target.Username);

		var resetRequest = new HttpRequestMessage(HttpMethod.Post, $"api/admin/accounts/{targetRow.Id}/reset-password")
		{
			Content = JsonContent.Create(new { NewPassword = "admin-reset-pass-1" })
		};
		resetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		await Assert.That((await http.SendAsync(resetRequest)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var authenticated = await accountService.AuthenticateAsync(target.Username, "admin-reset-pass-1");
		await Assert.That(authenticated!.MustChangePassword).IsTrue();
	}

	[Test]
	public async Task AccountLogin_ReturnsRoleAndPermissions()
	{
		var (http, account) = await RegisterAccountAsync();
		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(account.Username, Password));
		var body = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(body!.Role).IsEqualTo("Guest"); // no characters yet → Guest
	}
```

Extend the local `AccountLoginResponse` record in this file with `string Role, IReadOnlyList<string> Permissions`.

- [ ] **Step 2: Verify failure** — 404 on `api/admin/accounts`; missing `Role` field deserializes as null → assertion fails.

- [ ] **Step 3: Extract AccountClaimsService**

Create `SharpMUSH.Server/Authentication/AccountClaimsService.cs`; move the two private methods (`ComputeAccountRoleAsync`, `ComputeGrantedScopesAsync`) out of `JwtService.cs` verbatim, making them public instance methods; carry over exactly the dependencies those bodies use (look at `JwtService`'s constructor — likely `IAccountService`, `IRoleDerivationService`, `IPermissionResolver`). Register in `Startup.cs` next to `services.AddSingleton<IJwtService, JwtService>()` sites (both branches):

```csharp
		services.AddSingleton<AccountClaimsService>();
```

(Register it unconditionally near the other always-on services so it exists even when JWT isn't configured — `AuthController` needs it regardless.) Change `JwtService` to inject `AccountClaimsService` and delegate.

- [ ] **Step 4: Extend AccountLoginResponse**

In `AuthController`:

```csharp
	public record AccountLoginResponse(string AccountId, string Username, IReadOnlyList<CharacterSummary> Characters,
		string AccountSessionToken, bool MustChangePassword, string Role, IReadOnlyList<string> Permissions);
```

In `AccountLogin` and `AccountRegister`, compute before returning:

```csharp
		var role = await accountClaims.ComputeAccountRoleAsync(account.Id!);
		var permissions = await accountClaims.ComputeGrantedScopesAsync(account.Id!, role);
```

(match the moved method signatures — if `ComputeGrantedScopesAsync` takes different parameters, adapt the call, not the method). Inject `AccountClaimsService accountClaims` into `AuthController`'s ctor. Registration returns `Role = PortalRole.Guest.ToString()` naturally (no characters). Update the `DebugOttResponse` account payload only if trivially compatible — otherwise leave it.

- [ ] **Step 5: Implement AdminAccountsController**

`SharpMUSH.Server/Controllers/AdminAccountsController.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Helpers;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Admin-only account management. Authenticates with the account session bearer
/// (same scheme as AccountController) and requires the account's derived role to be
/// Wizard or God — enforced server-side per request, independent of the portal UI gate.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
public class AdminAccountsController(
	IAccountService accountService,
	IAccountSessionStore accountSessionStore,
	AccountClaimsService accountClaims,
	Microsoft.Extensions.Logging.ILogger<AdminAccountsController> logger) : ControllerBase
{
	public record AdminCharacterSummary(int DbrefNumber, string Name);
	public record AdminAccountRow(string Id, string Username, string? Email, bool IsDisabled,
		bool MustChangePassword, IReadOnlyList<AdminCharacterSummary> Characters);
	public record ResetPasswordRequest(string NewPassword);

	private static string FullId(string key) => $"node_accounts/{key}";
	private static string KeyOf(SharpAccount account) => account.Id!.Split('/')[^1];

	private async Task<(string? AdminAccountId, IActionResult? Failure)> RequireWizardAsync()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return (null, Unauthorized("Invalid or expired account session."));

		var accountId = await accountSessionStore.ValidateAsync(header["Bearer ".Length..].Trim());
		if (accountId is null)
			return (null, Unauthorized("Invalid or expired account session."));

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || account.IsDisabled)
			return (null, Unauthorized("Account not found or disabled."));
		if (account.MustChangePassword)
			return (null, StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action."));

		var role = await accountClaims.ComputeAccountRoleAsync(accountId);
		if (role < PortalRole.Wizard)
			return (null, StatusCode(StatusCodes.Status403Forbidden, "Wizard role required."));

		return (accountId, null);
	}

	[HttpGet]
	public async Task<IActionResult> List([FromQuery] string? search = null)
	{
		var (_, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;

		var accounts = await accountService.GetAllAccountsAsync();
		if (!string.IsNullOrWhiteSpace(search))
			accounts = accounts.Where(a =>
				a.Username.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| (a.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

		var rows = new List<AdminAccountRow>();
		foreach (var account in accounts)
		{
			var characters = await accountService.GetCharactersAsync(account.Id!);
			rows.Add(new AdminAccountRow(KeyOf(account), account.Username, account.Email,
				account.IsDisabled, account.MustChangePassword,
				characters.Select(c => new AdminCharacterSummary(c.Object.Key, c.Object.Name)).ToList()));
		}
		return Ok(rows);
	}

	[HttpPost("{key}/reset-password")]
	public async Task<IActionResult> ResetPassword(string key, [FromBody] ResetPasswordRequest request)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
			return BadRequest("NewPassword must be at least 8 characters.");

		var result = await accountService.SetPasswordAsync(FullId(key), request.NewPassword, mustChangePassword: true);
		if (result.IsT1) return NotFound(result.AsT1.Value);
		await accountSessionStore.RevokeAllForAccountAsync(FullId(key));
		logger.LogInformation("Admin {AdminId} reset password for account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
		return NoContent();
	}

	[HttpPost("{key}/disable")]
	public async Task<IActionResult> Disable(string key)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		var result = await accountService.DisableAccountAsync(FullId(key));
		if (result.IsT1) return NotFound(result.AsT1.Value);
		logger.LogInformation("Admin {AdminId} disabled account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
		return NoContent();
	}

	[HttpPost("{key}/enable")]
	public async Task<IActionResult> Enable(string key)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		var result = await accountService.EnableAccountAsync(FullId(key));
		if (result.IsT1) return NotFound(result.AsT1.Value);
		logger.LogInformation("Admin {AdminId} enabled account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
		return NoContent();
	}

	[HttpDelete("{key}/characters/{dbrefNumber:int}")]
	public async Task<IActionResult> UnlinkCharacter(string key, int dbrefNumber)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		await accountService.UnlinkCharacterAsync(FullId(key), new DBRef(dbrefNumber));
		logger.LogInformation("Admin {AdminId} unlinked #{Dbref} from account {Key}", LogSanitizer.Sanitize(adminId), dbrefNumber, LogSanitizer.Sanitize(key));
		return NoContent();
	}
}
```

Note the Memgraph/Surreal `node_accounts/<key>` normalization makes `FullId` provider-safe.

- [ ] **Step 6: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/AdminAccountsApiTests/*"` → PASS
Regression: `.../AuthHttpControllerTests/*` (response shape is additive) → PASS. Fix any client-side `AccountLoginResponse` records that now miss fields (Task 14 does the real client work; deserialization of extra JSON fields is ignored, so nothing breaks).

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Server/ SharpMUSH.Tests.Integration/Auth/AdminAccountsApiTests.cs
git commit -m "feat: admin accounts API + account role/permissions in login response"
```

---

### Task 14: Client production auth — AccountAuthStateProvider

**Files:**
- Create: `SharpMUSH.Client/Authentication/AccountAuthStateProvider.cs`
- Modify: `SharpMUSH.Client/Services/AccountAuthService.cs` (persist Role/Permissions, raise change event)
- Modify: `SharpMUSH.Client/Program.cs:120-125` (replace OIDC branch)
- Test: `SharpMUSH.Tests.BUnit/Authentication/AccountAuthStateProviderTests.cs`

**Interfaces:**
- Consumes: `AccountLoginResponse.Role/Permissions` (Task 13), `PortalPermission.ClaimType` (`"perm"`), `PortalRole`.
- Produces: in production builds the portal's `AuthenticationStateProvider` reflects the account session: authenticated identity (type `"AccountSession"`) with `ClaimTypes.Name`, `ClaimTypes.Role`, and one `perm` claim per permission; anonymous when logged out. `AccountAuthService.AuthStateChanged` event fires on login/logout/register. Dev keeps `DebugAuthStateProvider`.

- [ ] **Step 1: Write failing bUnit test**

`SharpMUSH.Tests.BUnit/Authentication/AccountAuthStateProviderTests.cs` — this is a plain unit test of the provider (no rendering needed); follow the project's TUnit style:

```csharp
using System.Security.Claims;
using SharpMUSH.Client.Authentication;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.BUnit.Authentication;

public class AccountAuthStateProviderTests
{
	[TUnit.Core.Test]
	public async Task LoggedOut_ReturnsAnonymous()
	{
		var provider = new AccountAuthStateProvider(CreateAuthService(loggedIn: false));
		var state = await provider.GetAuthenticationStateAsync();
		await Assert.That(state.User.Identity?.IsAuthenticated ?? false).IsFalse();
	}

	[TUnit.Core.Test]
	public async Task LoggedIn_EmitsRoleAndPermissionClaims()
	{
		var provider = new AccountAuthStateProvider(
			CreateAuthService(loggedIn: true, username: "headwiz", role: "Wizard", permissions: ["players.view", "players.moderate"]));
		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity!.IsAuthenticated).IsTrue();
		await Assert.That(state.User.IsInRole("Wizard")).IsTrue();
		await Assert.That(state.User.HasClaim(PortalPermission.ClaimType, "players.moderate")).IsTrue();
	}
}
```

`CreateAuthService(...)` needs an `AccountAuthService` in a known state. `AccountAuthService` takes `(IHttpClientFactory, IJSRuntime, ILogger<AccountAuthService>)` and holds state in properties with private setters — add an internal test hook OR (preferred) have the provider depend on a narrow interface. **Implement via interface:** create `IAccountAuthState` exposing `bool IsLoggedIn`, `string? Username`, `string? Role`, `IReadOnlyList<string> Permissions`, `event Action? AuthStateChanged`, implemented by `AccountAuthService`; the test then uses a tiny fake record-backed implementation.

- [ ] **Step 2: Verify failure** — compile error.

- [ ] **Step 3: Extend AccountAuthService**

Add to `AccountAuthService`:

```csharp
	private const string RoleKey = "sharpmush.account.role";
	private const string PermissionsKey = "sharpmush.account.permissions";

	public string? Role { get; private set; }
	public IReadOnlyList<string> Permissions { get; private set; } = [];

	/// <summary>Raised whenever login/logout changes the session; AccountAuthStateProvider subscribes.</summary>
	public event Action? AuthStateChanged;
```

- Update the private `AccountLoginResponse` record to include `string? Role, IReadOnlyList<string>? Permissions`.
- In `PersistSessionAsync`, accept and store role/permissions (sessionStorage; permissions JSON-serialized), set the properties, and fire `AuthStateChanged?.Invoke()` at the end.
- In `InitAsync`, load them back.
- In `LogoutAsync`, clear both keys, null the properties, fire `AuthStateChanged?.Invoke()`.
- Declare `public class AccountAuthService(...) : IAccountAuthState` with the new interface in `SharpMUSH.Client/Services/IAccountAuthState.cs`:

```csharp
namespace SharpMUSH.Client.Services;

public interface IAccountAuthState
{
	bool IsLoggedIn { get; }
	string? Username { get; }
	string? Role { get; }
	IReadOnlyList<string> Permissions { get; }
	event Action? AuthStateChanged;
}
```

Register in `Program.cs` right after the existing singleton:

```csharp
builder.Services.AddSingleton<IAccountAuthState>(sp => sp.GetRequiredService<AccountAuthService>());
```

- [ ] **Step 4: Implement the provider and swap OIDC**

`SharpMUSH.Client/Authentication/AccountAuthStateProvider.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Authentication;

/// <summary>
/// Production AuthenticationStateProvider backed by the account session
/// (replaces the never-configured OIDC wiring). Role and permission claims come
/// from the account-login response and drive [Authorize] / policy gates in the portal.
/// Server-side authorization is enforced independently per request.
/// </summary>
public class AccountAuthStateProvider : AuthenticationStateProvider
{
	private readonly IAccountAuthState _accountAuth;

	public AccountAuthStateProvider(IAccountAuthState accountAuth)
	{
		_accountAuth = accountAuth;
		_accountAuth.AuthStateChanged += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
	}

	public override Task<AuthenticationState> GetAuthenticationStateAsync()
	{
		if (!_accountAuth.IsLoggedIn)
			return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, _accountAuth.Username ?? "account"),
			new(ClaimTypes.Role, _accountAuth.Role ?? nameof(PortalRole.Player)),
		};
		claims.AddRange(_accountAuth.Permissions.Select(p => new Claim(PortalPermission.ClaimType, p)));

		var identity = new ClaimsIdentity(claims, "AccountSession");
		return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
	}
}
```

`Program.cs` — replace the `else` branch:

```csharp
if (builder.HostEnvironment.IsDevelopment())
{
	builder.Services.AddScoped<AuthenticationStateProvider, DebugAuthStateProvider>();
}
else
{
	builder.Services.AddScoped<AuthenticationStateProvider, AccountAuthStateProvider>();
}
```

Remove the now-unused `AddOidcAuthentication` call; if the OIDC package reference (`Microsoft.AspNetCore.Components.WebAssembly.Authentication`) is otherwise unused, leave the package for now (removing it may break `AddAuthorizationCore` usings) — note it as cleanup in Task 17 only if trivially safe.

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AccountAuthStateProviderTests/*"` → PASS
Run: `dotnet build` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/ SharpMUSH.Tests.BUnit/
git commit -m "feat: account-session AuthenticationStateProvider replaces vestigial OIDC in production"
```

---

### Task 15: Setup wizard page rework + global setup redirect + forced change-password

**Files:**
- Modify: `SharpMUSH.Client/Pages/Setup.razor`
- Modify: `SharpMUSH.Client/Layout/MainLayout.razor` (`EnsureAccountRoutingAsync`, lines 363-382)
- Modify: `SharpMUSH.Client/Pages/Account.razor` (forced mode)
- Test: `SharpMUSH.Tests.BUnit/Pages/SetupPageTests.cs`

**Interfaces:**
- Consumes: `api/setup/status` / `api/setup/complete` semantics (Task 7); `AccountAuthService.NeedsSetupAsync/CompleteSetupAsync` (existing, unchanged signatures); `AccountAuthService.MustChangePassword`.
- Produces: while `NeedsSetup`, every route except `/setup` redirects to `/setup`; the wizard explains it claims the pre-generated admin (rename + password) and handles 409 ("someone else completed setup" / username taken); `MustChangePassword` shows a forced-change view on `/account` (other profile sections hidden).

- [ ] **Step 1: Write failing bUnit test**

`SharpMUSH.Tests.BUnit/Pages/SetupPageTests.cs` — pattern on `SharpMUSH.Tests.BUnit/Pages/WikiRoutePageTests.cs` for page rendering with service fakes. `Setup.razor` injects `AccountAuthService` (concrete) — to make the page testable, first refactor its data needs behind the existing service: the page only calls `NeedsSetupAsync()` and `CompleteSetupAsync(...)`. Register a real `AccountAuthService` whose `IHttpClientFactory` returns a client backed by bUnit-friendly mock handler (`RichardSzalay.MockHttp` if already referenced — check the BUnit project's packages; otherwise write a small `FakeHttpMessageHandler` returning canned JSON):

```csharp
	[TUnit.Core.Test]
	public async Task Setup_ValidatesPasswordConfirmation()
	{
		// Arrange handler: GET api/setup/status -> {"needsSetup":true}
		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.Find("#setup-username").Change("headwiz");
		cut.Find("#setup-password").Change("password-one");
		cut.Find("#setup-confirm").Change("password-two");
		cut.Find("button.setup-submit").Click();

		await Assert.That(cut.Find(".setup-error").TextContent).Contains("do not match");
	}

	[TUnit.Core.Test]
	public async Task Setup_Conflict_ShowsClaimedMessage()
	{
		// Arrange handler: GET api/setup/status -> {"needsSetup":true};
		// POST api/setup/complete -> 409 with body "Setup has already been completed."
		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.Find("#setup-username").Change("headwiz");
		cut.Find("#setup-password").Change("password-one");
		cut.Find("#setup-confirm").Change("password-one");
		cut.Find("button.setup-submit").Click();

		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".setup-error").TextContent.Contains("completed by someone else")).IsTrue());
	}
```

Follow whatever HTTP-faking approach existing BUnit tests use (grep the project for `IHttpClientFactory` fakes first; mirror it).

- [ ] **Step 2: Verify failure / baseline** — run the new tests; validation may already pass (the page has confirm validation) — the 409 message test fails (no special handling today).

- [ ] **Step 3: Rework Setup.razor**

Keep the existing structure and ids; update copy and error handling in `Setup.razor`:

- Lead paragraph becomes:

```html
			<p class="setup-lead">
				This game hasn't been claimed yet. Choose a username and password for the
				pre-generated administrator account (linked to player #1, God). The first
				person to complete this form becomes the administrator.
			</p>
```

- In `SubmitAsync`, distinguish 409 responses: `CompleteSetupAsync` returns the raw error body; map it:

```csharp
		var (success, error) = await AccountAuth.CompleteSetupAsync(_username, _password);
		_busy = false;

		if (!success)
		{
			_error = error switch
			{
				not null when error.Contains("already been completed", StringComparison.OrdinalIgnoreCase)
					=> "Setup was just completed by someone else. If that wasn't you, contact the server operator immediately.",
				not null when error.Contains("already taken", StringComparison.OrdinalIgnoreCase)
					=> "That username is already taken — choose another.",
				_ => error ?? "Setup failed.",
			};
			StateHasChanged();
			return;
		}

		Nav.NavigateTo("/login", replace: true);
```

(Navigate to `/login` — the claimer must now log in with their new credentials.)

- [ ] **Step 4: Global redirect in MainLayout**

Replace `EnsureAccountRoutingAsync` (keep `IsHomePath` removal in mind):

```csharp
    private bool? _needsSetup;

    private async Task EnsureAccountRoutingAsync()
    {
        var currentPath = new Uri(NavigationManager.Uri).AbsolutePath;

        // First-run wizard: while the game is unclaimed, every route funnels to /setup.
        // Cache only the negative — once claimed, stop asking the server on each navigation.
        if (_needsSetup != false && !_isDebugAuth)
        {
            _needsSetup = await AccountAuth.NeedsSetupAsync();
            if (_needsSetup == true)
            {
                if (!string.Equals(currentPath, "/setup", StringComparison.OrdinalIgnoreCase))
                    NavigationManager.NavigateTo("/setup");
                return;
            }
        }

        if (AccountAuth.IsLoggedIn && AccountAuth.MustChangePassword)
        {
            if (!string.Equals(currentPath, "/account", StringComparison.OrdinalIgnoreCase))
                NavigationManager.NavigateTo("/account");
        }
    }
```

Delete the now-unused `IsHomePath`. Ensure `EnsureAccountRoutingAsync` is called from the `LocationChanged` handler as well as `OnInitializedAsync` (check the existing subscription at MainLayout.razor:216-253 — if `LocationChanged` doesn't currently re-run it, add it).

- [ ] **Step 5: Forced mode in Account.razor**

Wrap the non-password profile sections (username edit, email edit, character list, etc. — everything except the password block and the warning banner) in:

```razor
    @if (!AccountAuth.MustChangePassword)
    {
        ...existing sections...
    }
```

and strengthen the existing banner text: "You must change your password before using the rest of the portal."

- [ ] **Step 6: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/SetupPageTests/*"` → PASS
Run: `dotnet run --project SharpMUSH.Tests.BUnit` → all PASS (MainLayout is exercised by existing layout tests, if any).

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Client/ SharpMUSH.Tests.BUnit/
git commit -m "feat: setup wizard claims admin from any route; forced change-password view"
```

---

### Task 16: /admin/accounts portal page

**Files:**
- Create: `SharpMUSH.Client/Services/AdminAccountsService.cs`
- Create: `SharpMUSH.Client/Pages/Admin/AdminAccounts.razor`
- Modify: `SharpMUSH.Client/Program.cs` (register service)
- Modify: navigation registration for the admin section (find where `/admin/*` pages are added to nav — check `SharpMUSH.Client/Services/PortalNavSections.cs`; add an "Accounts" entry beside the existing admin links)
- Test: `SharpMUSH.Tests.BUnit/Pages/AdminAccountsPageTests.cs`

**Interfaces:**
- Consumes: `api/admin/accounts` routes/DTOs (Task 13), `AccountAuthService.AccountSessionToken`, `[Authorize(Policy = "players.moderate")]` (client `PermissionPolicyProvider` resolves `PortalPermission` scopes; Wizard/God built-in roles grant it).
- Produces: an admin page listing accounts with per-row actions (reset password via dialog, disable/enable, unlink character). Reset shows the entered temp password ONCE with "user must change it at next login".

- [ ] **Step 1: Write failing bUnit test**

`SharpMUSH.Tests.BUnit/Pages/AdminAccountsPageTests.cs` (pattern per `QuickLinksWidgetTests`/`WikiRoutePageTests`: `AddMudServices`, `Auth = AddAuthorization()`, loose JSInterop, fake HTTP as in Task 15):

```csharp
	[TUnit.Core.Test]
	public async Task RendersAccountRows_ForAuthorizedUser()
	{
		Auth.SetAuthorized("headwiz");
		Auth.SetPolicies("players.moderate");
		// fake GET api/admin/accounts -> two rows JSON
		var cut = Render<SharpMUSH.Client.Pages.Admin.AdminAccounts>();
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup.Contains("headwiz-target")).IsTrue();
			Assert.That(cut.Markup.Contains("DISABLED")).IsTrue();
		});
	}
```

Adapt assertion helpers to the sync/async forms used in neighboring tests.

- [ ] **Step 2: Verify failure** — page type doesn't exist.

- [ ] **Step 3: Client service**

`SharpMUSH.Client/Services/AdminAccountsService.cs`:

```csharp
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the admin accounts API (account-session bearer).</summary>
public class AdminAccountsService(IHttpClientFactory httpClientFactory, AccountAuthService accountAuth)
{
	public record AdminCharacterSummary(int DbrefNumber, string Name);
	public record AdminAccountRow(string Id, string Username, string? Email, bool IsDisabled,
		bool MustChangePassword, IReadOnlyList<AdminCharacterSummary> Characters);
	private record ResetPasswordRequest(string NewPassword);

	private HttpClient CreateClient()
	{
		var http = httpClientFactory.CreateClient("api");
		http.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accountAuth.AccountSessionToken);
		return http;
	}

	public async Task<IReadOnlyList<AdminAccountRow>> ListAsync(string? search = null)
	{
		var http = CreateClient();
		var url = string.IsNullOrWhiteSpace(search) ? "api/admin/accounts" : $"api/admin/accounts?search={Uri.EscapeDataString(search)}";
		return await http.GetFromJsonAsync<IReadOnlyList<AdminAccountRow>>(url) ?? [];
	}

	public async Task<(bool Success, string? Error)> ResetPasswordAsync(string key, string newPassword)
	{
		var response = await CreateClient().PostAsJsonAsync($"api/admin/accounts/{key}/reset-password", new ResetPasswordRequest(newPassword));
		return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
	}

	public async Task<(bool Success, string? Error)> SetDisabledAsync(string key, bool disabled)
	{
		var response = await CreateClient().PostAsync($"api/admin/accounts/{key}/{(disabled ? "disable" : "enable")}", null);
		return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
	}

	public async Task<(bool Success, string? Error)> UnlinkCharacterAsync(string key, int dbrefNumber)
	{
		var response = await CreateClient().DeleteAsync($"api/admin/accounts/{key}/characters/{dbrefNumber}");
		return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
	}
}
```

Register in `Program.cs` near the other client services: `builder.Services.AddSingleton<AdminAccountsService>();`

- [ ] **Step 4: The page**

`SharpMUSH.Client/Pages/Admin/AdminAccounts.razor` — follow `BannedNames.razor` structure (policy attribute, load-on-init, snackbar feedback). Content (Razor, 4-space indent):

```razor
@page "/admin/accounts"
@attribute [Authorize(Policy = "players.moderate")]
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Authorization
@using SharpMUSH.Client.Services
@inject AdminAccountsService AdminAccounts
@inject ISnackbar Snackbar

<PageTitle>Accounts — Admin — SharpMUSH</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Accounts</MudText>

<MudTextField T="string" @bind-Value="_search" Placeholder="Search username or email…"
              Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search"
              Immediate="true" DebounceInterval="300" OnDebounceIntervalElapsed="LoadAsync" Class="mb-4" />

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" />
}
else
{
    <MudTable Items="_rows" Dense="true" Hover="true">
        <HeaderContent>
            <MudTh>Username</MudTh>
            <MudTh>Email</MudTh>
            <MudTh>Status</MudTh>
            <MudTh>Characters</MudTh>
            <MudTh />
        </HeaderContent>
        <RowTemplate>
            <MudTd>@context.Username</MudTd>
            <MudTd>@(context.Email ?? "—")</MudTd>
            <MudTd>
                @if (context.IsDisabled)
                {
                    <MudChip T="string" Color="Color.Error" Size="Size.Small">DISABLED</MudChip>
                }
                else if (context.MustChangePassword)
                {
                    <MudChip T="string" Color="Color.Warning" Size="Size.Small">must change pw</MudChip>
                }
                else
                {
                    <MudChip T="string" Color="Color.Success" Size="Size.Small">active</MudChip>
                }
            </MudTd>
            <MudTd>
                @foreach (var character in context.Characters)
                {
                    <MudChip T="string" Size="Size.Small"
                             OnClose="@(() => UnlinkAsync(context, character))" CloseIcon="@Icons.Material.Filled.LinkOff">
                        @character.Name (#@character.DbrefNumber)
                    </MudChip>
                }
            </MudTd>
            <MudTd>
                <MudButton Size="Size.Small" OnClick="@(() => OpenResetAsync(context))">Reset password</MudButton>
                <MudButton Size="Size.Small" Color="@(context.IsDisabled ? Color.Success : Color.Error)"
                           OnClick="@(() => ToggleDisabledAsync(context))">
                    @(context.IsDisabled ? "Enable" : "Disable")
                </MudButton>
            </MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private IReadOnlyList<AdminAccountsService.AdminAccountRow> _rows = [];
    private string _search = string.Empty;
    private bool _loading = true;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _rows = await AdminAccounts.ListAsync(_search);
        _loading = false;
        StateHasChanged();
    }

    private async Task ToggleDisabledAsync(AdminAccountsService.AdminAccountRow row)
    {
        var (success, error) = await AdminAccounts.SetDisabledAsync(row.Id, !row.IsDisabled);
        Snackbar.Add(success
            ? $"Account '{row.Username}' {(row.IsDisabled ? "enabled" : "disabled")}."
            : error ?? "Operation failed.", success ? Severity.Success : Severity.Error);
        await LoadAsync();
    }

    private async Task UnlinkAsync(AdminAccountsService.AdminAccountRow row, AdminAccountsService.AdminCharacterSummary character)
    {
        var (success, error) = await AdminAccounts.UnlinkCharacterAsync(row.Id, character.DbrefNumber);
        Snackbar.Add(success ? $"Unlinked {character.Name} from '{row.Username}'." : error ?? "Unlink failed.",
            success ? Severity.Success : Severity.Error);
        await LoadAsync();
    }

    private async Task OpenResetAsync(AdminAccountsService.AdminAccountRow row)
    {
        var temp = $"temp-{Guid.NewGuid().ToString("N")[..10]}";
        var (success, error) = await AdminAccounts.ResetPasswordAsync(row.Id, temp);
        Snackbar.Add(success
            ? $"Password for '{row.Username}' reset to: {temp} — share it securely; they must change it at next login."
            : error ?? "Reset failed.", success ? Severity.Warning : Severity.Error, o => o.RequireInteraction = true);
        await LoadAsync();
    }
}
```

Adjust MudBlazor 9.x API details (e.g. `MudChip T=` requirement) to match neighboring pages. Add the nav entry per `PortalNavSections.cs` conventions (label "Accounts", href `/admin/accounts`, same permission gate the file uses for `players.*` items).

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AdminAccountsPageTests/*"` → PASS
Run: `dotnet build` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/ SharpMUSH.Tests.BUnit/
git commit -m "feat: /admin/accounts portal page — list, reset password, disable/enable, unlink"
```

---

### Task 17: Deployment & docs cleanup + full verification

**Files:**
- Modify: `deploy/docker-compose.prod.yml` (remove the two `SHARPMUSH_BOOTSTRAP_*` lines + their comment)
- Modify: `deploy/.env.example` (remove `BOOTSTRAP_USERNAME` / `BOOTSTRAP_PASSWORD` lines)
- Modify: `CLAUDE.md` (env-var list: remove bootstrap vars; describe the setup wizard in "Running the Server")
- Modify: `docs/todo/area-01-auth.md` (append completed items)
- Check: `deploy/` READMEs / `kubernetes/SETUP.md` for bootstrap references (`grep -rn "BOOTSTRAP" deploy/ kubernetes/ docs/ *.md`)

**Interfaces:**
- Consumes: everything above.
- Produces: a deployment with **zero** admin credentials in env/config; docs describing the first-run wizard.

- [ ] **Step 1: Remove bootstrap env from prod compose**

In `deploy/docker-compose.prod.yml`, delete:

```yaml
      # --- Initial God/admin account, created on first boot ---
      - SHARPMUSH_BOOTSTRAP_USERNAME=${BOOTSTRAP_USERNAME:?set BOOTSTRAP_USERNAME in deploy/.env}
      - SHARPMUSH_BOOTSTRAP_PASSWORD=${BOOTSTRAP_PASSWORD:?set BOOTSTRAP_PASSWORD in deploy/.env}
```

In `deploy/.env.example`, delete the `BOOTSTRAP_USERNAME=admin` and `BOOTSTRAP_PASSWORD=change-me-to-something-strong` lines; add in their place:

```bash
# First-run admin setup happens in the web portal: the first visitor to
# https://<your-domain>/setup after a fresh install claims the admin account.
# Deploy, then complete setup immediately.
```

- [ ] **Step 2: Update CLAUDE.md and docs**

- In `CLAUDE.md` "Key environment variables", remove the `SHARPMUSH_BOOTSTRAP_USERNAME / SHARPMUSH_BOOTSTRAP_PASSWORD` line and add: `First-run admin setup: web portal /setup (first visitor claims the pre-generated admin linked to #1); or set God's password in-game.`
- Sweep remaining references: `grep -rn "BOOTSTRAP" --include='*.md' --include='*.yml' --include='*.yaml' .` (excluding `docs/superpowers/`) and fix each.
- Append to `docs/todo/area-01-auth.md` under Implementation Tasks:

```markdown
- [x] First-run setup wizard claims pre-generated admin (ServerState.SetupCompleted; no bootstrap credentials)
- [x] Login matrix: character passwords/names accepted as account credentials
- [x] Server-side MustChangePassword enforcement; account disable; net logins/player_creation enforcement
- [x] debug-ott gated to Development; admin accounts API + /admin/accounts page; @account command
```

- [ ] **Step 3: Full verification**

```bash
dotnet build                                        # 0 errors, 0 warnings (TreatWarningsAsErrors)
dotnet run --project SharpMUSH.Tests                # all pass
dotnet run --project SharpMUSH.Tests.Integration    # all pass
dotnet run --project SharpMUSH.Tests.BUnit          # all pass
```

- [ ] **Step 4: Commit**

```bash
git add deploy/ CLAUDE.md docs/
git commit -m "docs/deploy: remove bootstrap credentials — first-run setup wizard replaces them"
```

---

## Plan Self-Review Notes

- **Spec coverage:** ServerState + inference (T1-2), credential-free bootstrap (T6), wizard claim + 409s (T7), telnet escape hatch via God password + `@account/setupcomplete` (T5, T12), login matrix with empty-hash rule and legacy rehash (T4), debug-ott gate (T8), MustChangePassword enforcement (T9), PlayerCreation (T10), Logins (T11), disable + revoke (T3), `@account` family (T12), admin API + page (T13, T16), OIDC replacement + forced change-password + any-route redirect (T14, T15), deployment cleanup (T17). Out-of-scope items untouched.
- **Known adaptation points** (executor must match live code, flagged inline): Core.Arango migrator API shapes, TUnit `NotInParallel`/`Order` syntax, Mediator handler generic shapes, `Notify` overloads, MudBlazor 9 component params, BUnit HTTP-faking convention, moved `JwtService` helper signatures.
- **Ordering:** T1→T2 (interface stubs), T3→T4/T6/T12/T13, T5→T7/T12, T13→T14→T16. T8-T11 independent after T3.
