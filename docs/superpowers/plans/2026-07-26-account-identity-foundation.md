# Account Identity Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an account a permanent, safely-referenceable identity — closing or deleting an account becomes a status transition rather than a row removal — and make the account id and character objid on a request principal correct and unambiguous.

**Architecture:** `SharpAccount.IsDisabled` (bool) becomes an `AccountStatus` enum with four states, and `DeleteAccountAsync` becomes `SetAccountStatusAsync`, so account documents are never removed. A reserved `system` account owns server-authored content. On the request side, `ClaimTypes.NameIdentifier` becomes uniformly "account id" across all three authentication handlers. (The objid transport contract originally planned here as Tasks 6-7 has since been implemented separately — see the note where those tasks were.)

This is phase 1 of the spec at `docs/superpowers/specs/2026-07-26-wiki-account-attribution-design.md`. It touches no wiki code. Phase 2 (attribution edges, asset metadata into the database) depends on it.

**Tech Stack:** .NET 10, C# 14, TUnit (not xUnit/MSTest), NSubstitute, bUnit, OneOf, source-generated Mediator, ArangoDB (primary) / Memgraph / SurrealDB, Blazor WASM + MudBlazor 9.

## Global Constraints

- **C# files:** tabs, indent size 2. **Enforced** — a build failing with `FORMAT001` is fixed by `dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"`, run until it reports no changes (**the formatter needs two passes to converge**).
- **Razor files:** spaces, indent size 4 (not enforced; `dotnet format` does not process `.razor`).
- **Line endings:** LF.
- **`TreatWarningsAsErrors` is enabled in most projects.** Never disable it to make a build pass — fix the warning.
- **Never introduce nullable returns from services.** Use `OneOf<T, Error<string>>` / `OneOf<T, NotFound>`. (Existing `SharpAccount?` lookup returns are pre-existing and stay as they are; do not add new ones.)
- Prefer `var`; no `this.` qualifier.
- **Do not add narrating or explanatory code comments.** Comment only what the code cannot say — a non-obvious invariant or a reason. Let the code and tests speak.
- Enum values are persisted **as strings**. A **missing** status field reads back as
  `AccountStatus.Active` (documents written before the field existed are active accounts); an
  **unparseable** value reads back as `AccountStatus.Disabled`. The asymmetry is deliberate:
  `Status` gates authentication, so an unrecognised value must fail closed. A corrupt or
  future-version value locking an account out is recoverable by an admin; one silently
  re-enabling a closed account is not.
- Run all tests with `dotnet run --project SharpMUSH.Tests` (TUnit). Filter with `--treenode-filter "/*/*/<ClassName>/*"`.
- Blazor component tests live in `SharpMUSH.Tests.BUnit`, run with `dotnet run --project SharpMUSH.Tests.BUnit`.

## File Structure

**Created:**
- `SharpMUSH.Library/Models/AccountStatus.cs` — the four-state enum.
- `SharpMUSH.Library/Definitions/SystemAccount.cs` — the reserved username constant, so server and client agree on one spelling.
- `SharpMUSH.Tests/Services/AccountStatusTests.cs` — lifecycle transitions, guards, login gating.

**Modified (grouped by responsibility):**
- *Model + contract:* `SharpAccount.cs`, `ISharpDatabase.cs`, `IAccountService.cs`, `AccountService.cs`
- *Persistence:* `ArangoDatabase.Accounts.cs`, `MemgraphDatabase.Accounts.cs`, `SurrealDatabase.Accounts.cs`, `Migrations/Migration_AddAccounts.cs`
- *Bootstrap:* `BootstrapService.cs`
- *In-game surface:* `AccountAdminCommands.cs`, `ErrorMessages.cs`, `Notifications.resx`, `Notifications.fr.resx`
- *HTTP surface:* `AdminAccountsController.cs`, `AccountController.cs`, `AuthController.cs`, `RolesController.cs`, `MailController.cs`
- *Auth:* `MushBasicAuthenticationHandler.cs` (Task 8 only — the other handlers and the claims accessor are already done)
- *Client:* `AdminAccountsService.cs`, `AdminAccounts.razor`, `AccountRolesModel.cs`, `AdminRoles.razor`, `SharedResource.resx`, `SharedResource.fr.resx`

---

### Task 1: Replace `IsDisabled` with `AccountStatus`

The C# compiler forces this to be one task: renaming the property breaks every consumer at once. The deliverable is a green build and a green existing test suite, plus one new test proving login is gated on all three non-`Active` states.

**Files:**
- Create: `SharpMUSH.Library/Models/AccountStatus.cs`
- Modify: `SharpMUSH.Library/Models/SharpAccount.cs:30`
- Modify: `SharpMUSH.Library/ISharpDatabase.cs` (the `UpdateAccountDisabledAsync` declaration, near line 776-790)
- Modify: `SharpMUSH.Library/Services/AccountService.cs:35,175,206`
- Modify: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Accounts.cs:74,200-206,234`
- Modify: `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddAccounts.cs:40`
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Accounts.cs:50,152-158,182`
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Accounts.cs:21,28,76,174-184,204`
- Modify: `SharpMUSH.Server/Controllers/AccountController.cs:46`
- Modify: `SharpMUSH.Server/Controllers/AuthController.cs:85,178`
- Modify: `SharpMUSH.Server/Controllers/AdminAccountsController.cs:28,46,72`
- Modify: `SharpMUSH.Server/Controllers/RolesController.cs:45,136`
- Modify: `SharpMUSH.Server/Authentication/AccountSessionAuthenticationHandler.cs:40`
- Modify: `SharpMUSH.Implementation/Commands/AccountAdminCommands.cs:38,108`
- Modify: `SharpMUSH.Client/Services/AdminAccountsService.cs:9`
- Modify: `SharpMUSH.Client/Pages/Admin/AdminAccounts.razor:32,56,58,87,89`
- Modify: `SharpMUSH.Client/Models/Roles/AccountRolesModel.cs:11`
- Modify: `SharpMUSH.Client/Pages/Admin/Roles/AdminRoles.razor:191`
- Test: `SharpMUSH.Tests/Services/AccountStatusTests.cs` (create)
- Test: `SharpMUSH.Tests/Services/AccountServiceTests.cs:44-54` (update helper)
- Test: `SharpMUSH.Tests/Database/AccountAdminDbTests.cs:19,23`
- Test: `SharpMUSH.Tests/Authentication/AccountSessionAuthHandlerTests.cs:36`
- Test: `SharpMUSH.Tests.BUnit/Pages/AdminAccountsPageTests.cs:48,57`
- Test: `SharpMUSH.Tests.Integration/Auth/AdminAccountsApiTests.cs:46`

**Interfaces:**
- Produces: `AccountStatus` enum (`Active`, `Disabled`, `Closed`, `Deleted`); `SharpAccount.Status` (get/set, defaults to `Active`); `SharpAccount.IsActive` (computed `bool`); `ISharpDatabase.UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken ct = default) -> ValueTask`. Every later task in this plan and all of phase 2 depend on these names.
- Consumes: nothing.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Services/AccountStatusTests.cs`:

```csharp
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class AccountStatusTests
{
	private static (AccountService Service, ISharpDatabase Db, IPasswordService Passwords) Build()
	{
		var db = Substitute.For<ISharpDatabase>();
		var pw = Substitute.For<IPasswordService>();
		var sessions = Substitute.For<IAccountSessionStore>();

		db.GetPlayerByNameOrAliasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Enumerable.Empty<SharpPlayer>().ToAsyncEnumerable());
		db.GetCharactersForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new List<SharpPlayer>());

		return (new AccountService(db, pw, sessions), db, pw);
	}

	private static SharpAccount MakeAccount(AccountStatus status) => new()
	{
		Id = "node_accounts/1",
		Username = "TestUser",
		PasswordHash = "hash",
		CreatedAt = 1_000_000,
		Status = status
	};

	[Test]
	[Arguments(null, AccountStatus.Active)]
	[Arguments("", AccountStatus.Active)]
	[Arguments("Active", AccountStatus.Active)]
	[Arguments("Closed", AccountStatus.Closed)]
	[Arguments("Deleted", AccountStatus.Deleted)]
	[Arguments("Banished", AccountStatus.Disabled)]
	[Arguments("garbage", AccountStatus.Disabled)]
	public async ValueTask ParseStatus_MissingIsActive_UnparseableFailsClosed(string? stored, AccountStatus expected)
	{
		await Assert.That(AccountStatusParser.Parse(stored)).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask NewAccount_DefaultsToActive()
	{
		var account = new SharpAccount { Username = "Fresh", PasswordHash = "hash" };

		await Assert.That(account.Status).IsEqualTo(AccountStatus.Active);
		await Assert.That(account.IsActive).IsTrue();
	}

	[Test]
	[Arguments(AccountStatus.Disabled)]
	[Arguments(AccountStatus.Closed)]
	[Arguments(AccountStatus.Deleted)]
	public async ValueTask Authenticate_NonActiveStatus_ReturnsNull(AccountStatus status)
	{
		var (svc, db, pw) = Build();
		db.GetAccountByUsernameAsync("TestUser", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(status));
		pw.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

		var result = await svc.AuthenticateAsync("TestUser", "correct-password");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask Authenticate_ActiveStatus_ReturnsAccount()
	{
		var (svc, db, pw) = Build();
		db.GetAccountByUsernameAsync("TestUser", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(AccountStatus.Active));
		pw.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

		var result = await svc.AuthenticateAsync("TestUser", "correct-password");

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Username).IsEqualTo("TestUser");
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountStatusTests/*"`
Expected: compile failure — `'SharpAccount' does not contain a definition for 'Status'` and `AccountStatus` not found.

- [ ] **Step 3: Create the enum**

Create `SharpMUSH.Library/Models/AccountStatus.cs`:

```csharp
namespace SharpMUSH.Library.Models;

/// <summary>
/// The single lifecycle state of a <see cref="SharpAccount"/>. Account documents are never
/// deleted, so historical references to an account always resolve; <see cref="Closed"/> and
/// <see cref="Deleted"/> are terminal-by-intent rather than removals.
/// </summary>
public enum AccountStatus
{
	Active,
	Disabled,
	Closed,
	Deleted
}
```

Add the shared parser alongside it, in the same file, so all three providers use one
implementation of the fail-open/fail-closed rule:

```csharp
namespace SharpMUSH.Library.Models;

public static class AccountStatusParser
{
	/// <summary>
	/// Reads a persisted status. A <see langword="null"/> or empty value means the field was never
	/// written, which is an active account. Any other unrecognised value fails closed to
	/// <see cref="AccountStatus.Disabled"/>: <see cref="SharpAccount.Status"/> gates authentication,
	/// and a corrupt value must not be able to re-enable a closed account.
	/// </summary>
	public static AccountStatus Parse(string? stored)
		=> string.IsNullOrEmpty(stored)
			? AccountStatus.Active
			: Enum.TryParse<AccountStatus>(stored, out var parsed)
				? parsed
				: AccountStatus.Disabled;
}
```

Each provider's mapper calls this as `ParseStatus`; add
`private static AccountStatus ParseStatus(string? stored) => AccountStatusParser.Parse(stored);`
to each, or call `AccountStatusParser.Parse` directly.

- [ ] **Step 4: Replace the property on the model**

In `SharpMUSH.Library/Models/SharpAccount.cs`, replace:

```csharp
	public bool IsDisabled { get; set; }
```

with:

```csharp
	public AccountStatus Status { get; set; } = AccountStatus.Active;

	/// <summary>
	/// Whether this account may be used to log in. Derived from <see cref="Status"/> so there is
	/// no second field that could contradict it.
	/// </summary>
	public bool IsActive => Status == AccountStatus.Active;
```

Also update the class doc comment's final line to note that the account is never deleted:

```csharp
/// A web/account-layer identity that owns zero or more MUSH characters.
/// Stored in <c>node_accounts</c> — has no MUSH dbref and no in-game presence.
/// Characters are linked via <c>edge_account_owns_character</c> graph edges.
/// Documents are never removed; see <see cref="AccountStatus"/>.
```

- [ ] **Step 5: Rename the database contract method**

In `SharpMUSH.Library/ISharpDatabase.cs`, replace the `UpdateAccountDisabledAsync` declaration with:

```csharp
	/// <summary>
	/// Sets the account's lifecycle status. Account documents are never removed, so this is the
	/// only way an account leaves <see cref="AccountStatus.Active"/>.
	/// </summary>
	ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default);
```

Leave `DeleteAccountAsync` in place for now — Task 2 removes it, and removing it here would break the three providers mid-task.

- [ ] **Step 6: Update the ArangoDB provider**

In `SharpMUSH.Database.ArangoDB/ArangoDatabase.Accounts.cs`:

In `CreateAccountAsync`, replace `IsDisabled = false` in the `doc` initializer with:

```csharp
			Status = nameof(AccountStatus.Active)
```

Replace `UpdateAccountDisabledAsync` with:

```csharp
	public async ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(accountId);
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Accounts,
			new { _key = key, Status = status.ToString(), UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
			mergeObjects: true, cancellationToken: cancellationToken);
	}
```

In `AccountFromJson`, replace the `IsDisabled` line with:

```csharp
			Status = ParseStatus(elem.TryGetProperty("Status", out var statusProp) ? statusProp.GetString() : null)
```

- [ ] **Step 7: Update the ArangoDB schema rule**

In `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddAccounts.cs`, replace the schema property:

```csharp
							IsDisabled = new { type = DatabaseConstants.TypeBoolean }
```

with:

```csharp
							Status = new { type = DatabaseConstants.TypeString }
```

- [ ] **Step 8: Update the Memgraph provider**

In `SharpMUSH.Database.Memgraph/MemgraphDatabase.Accounts.cs`:

In the `CREATE (a:Account { ... })` Cypher in `CreateAccountAsync`, replace `isDisabled: false` with `status: 'Active'`.

Replace `UpdateAccountDisabledAsync` with:

```csharp
	public async ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) SET a.status = $status, a.updatedAt = $now",
			new { id = key, status = status.ToString(), now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, cancellationToken);
	}
```

In `MapNodeToAccount`, replace the `IsDisabled` line with:

```csharp
			Status = ParseStatus(node.Properties.TryGetValue("status", out var status) ? status?.ToString() : null)
```

- [ ] **Step 9: Update the SurrealDB provider**

In `SharpMUSH.Database.SurrealDB/SurrealDatabase.Accounts.cs`:

On `AccountDbRecord`, replace `public bool isDisabled { get; set; }` with:

```csharp
	public string status { get; set; } = nameof(AccountStatus.Active);
```

In `AccountFieldSelection`, replace the trailing `isDisabled` with `status`.

In the `CREATE account CONTENT { ... }` SurrealQL in `CreateAccountAsync`, replace `isDisabled: false` with `status: 'Active'`.

Replace `UpdateAccountDisabledAsync` with:

```csharp
	public async ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		await ExecuteAsync("UPDATE $accountId SET status = $status, updatedAt = $now",
			new Dictionary<string, object?>
			{
				["accountId"] = new StringRecordId(key),
				["status"] = status.ToString(),
				["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}, cancellationToken);
	}
```

In `MapRecordToAccount`, replace the `IsDisabled = rec.isDisabled` line with:

```csharp
		Status = ParseStatus(rec.status),
```

- [ ] **Step 10: Update `AccountService`**

In `SharpMUSH.Library/Services/AccountService.cs`:

Replace the login gate:

```csharp
		if (account is null || account.IsDisabled)
			return null;
```

with:

```csharp
		if (account is null || !account.IsActive)
			return null;
```

In `DisableAccountAsync`, replace `await database.UpdateAccountDisabledAsync(accountId, true, ct);` with:

```csharp
		await database.UpdateAccountStatusAsync(accountId, AccountStatus.Disabled, ct);
```

In `EnableAccountAsync`, replace `await database.UpdateAccountDisabledAsync(accountId, false, ct);` with:

```csharp
		await database.UpdateAccountStatusAsync(accountId, AccountStatus.Active, ct);
```

- [ ] **Step 11: Update the remaining server read sites**

Each of these is the same edit — `account.IsDisabled` becomes `!account.IsActive`:

- `SharpMUSH.Server/Controllers/AccountController.cs:46` — `if (account is null || !account.IsActive)`
- `SharpMUSH.Server/Controllers/AuthController.cs:85` — `if (sessionAccount is null || !sessionAccount.IsActive)`
- `SharpMUSH.Server/Controllers/AuthController.cs:178` — `if (account is null || !account.IsActive)`
- `SharpMUSH.Server/Controllers/AdminAccountsController.cs:46` — `if (account is null || !account.IsActive)`
- `SharpMUSH.Server/Authentication/AccountSessionAuthenticationHandler.cs:40` — `if (account is null || !account.IsActive)`

In `AdminAccountsController.cs`, the DTO on line 28 changes shape:

```csharp
	public record AdminAccountRow(string Id, string Username, string? Email, string Status,
```

and the projection on line 72 becomes:

```csharp
				account.Status.ToString(), account.MustChangePassword,
```

In `RolesController.cs`, the DTO field on line 45 becomes `string Status,` and the projection on line 136 becomes `account.Status.ToString(),`.

- [ ] **Step 12: Update the in-game command display**

In `SharpMUSH.Implementation/Commands/AccountAdminCommands.cs`, replace the list line:

```csharp
				$"{a.Username,-30} {(a.IsDisabled ? "DISABLED" : "active"),-10} {(a.MustChangePassword ? "must-change-pw" : string.Empty)}");
```

with:

```csharp
				$"{a.Username,-30} {StatusLabel(a.Status),-10} {(a.MustChangePassword ? "must-change-pw" : string.Empty)}");
```

and the detail line:

```csharp
			$"Status: {(account.IsDisabled ? "DISABLED" : "active")}{(account.MustChangePassword ? ", must change password" : string.Empty)}\n" +
```

with:

```csharp
			$"Status: {StatusLabel(account.Status)}{(account.MustChangePassword ? ", must change password" : string.Empty)}\n" +
```

Add this private helper to the same `Commands` partial class, below the `AccountAdmin` method:

```csharp
	private static string StatusLabel(AccountStatus status) => status switch
	{
		AccountStatus.Active => "active",
		_ => status.ToString().ToUpperInvariant()
	};
```

Add `using SharpMUSH.Library.Models;` to the file's usings if it is not already present.

- [ ] **Step 13: Update the client**

In `SharpMUSH.Client/Services/AdminAccountsService.cs:9`, change the record field:

```csharp
	public record AdminAccountRow(string Id, string Username, string? Email, string Status,
```

In `SharpMUSH.Client/Models/Roles/AccountRolesModel.cs:11`, change `bool IsDisabled,` to `string Status,`.

In `SharpMUSH.Client/Pages/Admin/Roles/AdminRoles.razor:191`, change `@if (_account.IsDisabled)` to:

```razor
                                @if (_account.Status != "Active")
```

In `SharpMUSH.Client/Pages/Admin/AdminAccounts.razor`, change line 32 `@if (context.IsDisabled)` to `@if (context.Status != "Active")`; lines 56-58 to:

```razor
                <MudButton Size="Size.Small" Color="@(context.Status == "Active" ? Color.Error : Color.Success)"
                           OnClick="@(() => ToggleDisabled(context))">
                    @(context.Status == "Active" ? "Disable" : "Enable")
```

and lines 87-89 to:

```razor
        var (success, error) = await AccountsService.SetDisabledAsync(row.Id, row.Status == "Active");
        var message = success
            ? $"Account '{row.Username}' {(row.Status == "Active" ? "disabled" : "enabled")}."
```

Task 5 replaces this two-state toggle with a full status control; this step only keeps it compiling and behaviourally identical.

- [ ] **Step 14: Update the existing tests**

- `SharpMUSH.Tests/Services/AccountServiceTests.cs:44-54` — change the `MakeAccount` helper's parameter from `bool isDisabled = false` to `AccountStatus status = AccountStatus.Active` and its initializer line from `IsDisabled = isDisabled` to `Status = status`. Update any call site passing `isDisabled: true` to `status: AccountStatus.Disabled`.
- `SharpMUSH.Tests/Database/AccountAdminDbTests.cs:19,23` — replace `reloaded!.IsDisabled).IsTrue()` with `reloaded!.Status).IsEqualTo(AccountStatus.Disabled)` and `IsFalse()` with `IsEqualTo(AccountStatus.Active)`. Update the `UpdateAccountDisabledAsync` calls in that file to `UpdateAccountStatusAsync(id, AccountStatus.Disabled)` / `(id, AccountStatus.Active)`.
- `SharpMUSH.Tests/Authentication/AccountSessionAuthHandlerTests.cs:36` — replace `IsDisabled = isDisabled,` with `Status = isDisabled ? AccountStatus.Disabled : AccountStatus.Active,`.
- `SharpMUSH.Tests.BUnit/Pages/AdminAccountsPageTests.cs:48,57` — replace `IsDisabled = false,` with `Status = "Active",` and `IsDisabled = true,` with `Status = "Disabled",`.
- `SharpMUSH.Tests.Integration/Auth/AdminAccountsApiTests.cs:46` — change the local record to `private record AdminAccountRow(string Id, string Username, string? Email, string Status, bool MustChangePassword);`.

Add `using SharpMUSH.Library.Models;` to each test file that now names `AccountStatus`.

- [ ] **Step 15: Run the new tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountStatusTests/*"`
Expected: PASS — 5 tests (1 default + 3 parameterised non-active + 1 active).

- [ ] **Step 16: Run the full suite to verify nothing regressed**

Run: `dotnet run --project SharpMUSH.Tests`
Then: `dotnet run --project SharpMUSH.Tests.BUnit`
Expected: PASS. A `FORMAT001` failure means run the formatter command from Global Constraints, twice.

- [ ] **Step 17: Commit**

```bash
git add SharpMUSH.Library SharpMUSH.Database.ArangoDB SharpMUSH.Database.Memgraph SharpMUSH.Database.SurrealDB SharpMUSH.Server SharpMUSH.Implementation SharpMUSH.Client SharpMUSH.Tests SharpMUSH.Tests.BUnit SharpMUSH.Tests.Integration
git commit -m "Replace SharpAccount.IsDisabled with a four-state AccountStatus

A bool plus a future enum could contradict each other, so status is one
field with one truth, and IsActive derives from it rather than shadowing
it. Persisted as a string; an unparseable or absent stored value reads
back as Active."
```

---

### Task 2: `SetAccountStatusAsync`, and accounts stop being deletable

**Files:**
- Modify: `SharpMUSH.Library/Services/Interfaces/IAccountService.cs:64` (replace `DeleteAccountAsync`)
- Modify: `SharpMUSH.Library/Services/AccountService.cs:170-207`
- Modify: `SharpMUSH.Library/ISharpDatabase.cs` (remove `DeleteAccountAsync`)
- Modify: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Accounts.cs:116-130` (remove)
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Accounts.cs:97` (remove)
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Accounts.cs:111` (remove)
- Test: `SharpMUSH.Tests/Services/AccountStatusTests.cs` (extend)

**Interfaces:**
- Consumes: `AccountStatus`, `SharpAccount.Status`, `ISharpDatabase.UpdateAccountStatusAsync` from Task 1.
- Produces: `IAccountService.SetAccountStatusAsync(string accountId, AccountStatus status, CancellationToken ct = default) -> ValueTask<OneOf<Success, Error<string>>>`; `CloseAccountAsync(string accountId, CancellationToken ct = default)`; `MarkAccountDeletedAsync(string accountId, CancellationToken ct = default)` — both with the same return type. `DisableAccountAsync` and `EnableAccountAsync` keep their existing signatures.

- [ ] **Step 1: Write the failing test**

Append to `SharpMUSH.Tests/Services/AccountStatusTests.cs` (inside the class):

```csharp
	[Test]
	[Arguments(AccountStatus.Disabled)]
	[Arguments(AccountStatus.Closed)]
	[Arguments(AccountStatus.Deleted)]
	public async ValueTask SetAccountStatus_RevokesSessionsAndPersists(AccountStatus status)
	{
		var db = Substitute.For<ISharpDatabase>();
		var pw = Substitute.For<IPasswordService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		var svc = new AccountService(db, pw, sessions);

		db.GetAccountByIdAsync("node_accounts/1", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(AccountStatus.Active));

		var result = await svc.SetAccountStatusAsync("node_accounts/1", status);

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountStatusAsync("node_accounts/1", status, Arg.Any<CancellationToken>());
		await sessions.Received(1).RevokeAllForAccountAsync("node_accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask SetAccountStatus_Active_DoesNotRevokeSessions()
	{
		var db = Substitute.For<ISharpDatabase>();
		var pw = Substitute.For<IPasswordService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		var svc = new AccountService(db, pw, sessions);

		db.GetAccountByIdAsync("node_accounts/1", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(AccountStatus.Closed));

		var result = await svc.SetAccountStatusAsync("node_accounts/1", AccountStatus.Active);

		await Assert.That(result.IsT0).IsTrue();
		await sessions.DidNotReceive().RevokeAllForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask SetAccountStatus_UnknownAccount_ReturnsError()
	{
		var db = Substitute.For<ISharpDatabase>();
		var svc = new AccountService(db, Substitute.For<IPasswordService>(), Substitute.For<IAccountSessionStore>());

		db.GetAccountByIdAsync("node_accounts/404", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.SetAccountStatusAsync("node_accounts/404", AccountStatus.Closed);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).IsEqualTo("Account not found.");
	}

	[Test]
	public async ValueTask CloseAndMarkDeleted_SetTheirRespectiveStatuses()
	{
		var db = Substitute.For<ISharpDatabase>();
		var svc = new AccountService(db, Substitute.For<IPasswordService>(), Substitute.For<IAccountSessionStore>());
		db.GetAccountByIdAsync("node_accounts/1", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(AccountStatus.Active));

		await svc.CloseAccountAsync("node_accounts/1");
		await svc.MarkAccountDeletedAsync("node_accounts/1");

		await db.Received(1).UpdateAccountStatusAsync("node_accounts/1", AccountStatus.Closed, Arg.Any<CancellationToken>());
		await db.Received(1).UpdateAccountStatusAsync("node_accounts/1", AccountStatus.Deleted, Arg.Any<CancellationToken>());
	}
```

Add `using OneOf.Types;` to the file's usings.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountStatusTests/*"`
Expected: compile failure — `'AccountService' does not contain a definition for 'SetAccountStatusAsync'`.

- [ ] **Step 3: Replace the interface method**

In `SharpMUSH.Library/Services/Interfaces/IAccountService.cs`, replace:

```csharp
	ValueTask DeleteAccountAsync(string accountId, CancellationToken ct = default);
```

with:

```csharp
	/// <summary>
	/// Sets the account's lifecycle status. Accounts are never removed, so this is how an account
	/// is disabled, closed, deleted, or restored. Any transition away from
	/// <see cref="AccountStatus.Active"/> revokes live sessions.
	/// Returns an error if the account is not found, or if it is the reserved system account.
	/// </summary>
	ValueTask<OneOf<Success, Error<string>>> SetAccountStatusAsync(string accountId, AccountStatus status, CancellationToken ct = default);

	/// <summary>Marks the account closed — the holder has left. Reversible by an admin.</summary>
	ValueTask<OneOf<Success, Error<string>>> CloseAccountAsync(string accountId, CancellationToken ct = default);

	/// <summary>Marks the account deleted. The document is retained; see <see cref="AccountStatus"/>.</summary>
	ValueTask<OneOf<Success, Error<string>>> MarkAccountDeletedAsync(string accountId, CancellationToken ct = default);
```

Add `using SharpMUSH.Library.Models;` if not already present.

- [ ] **Step 4: Implement on `AccountService`**

In `SharpMUSH.Library/Services/AccountService.cs`, replace:

```csharp
	public ValueTask DeleteAccountAsync(string accountId, CancellationToken ct = default)
		=> database.DeleteAccountAsync(accountId, ct);
```

with:

```csharp
	public async ValueTask<OneOf<Success, Error<string>>> SetAccountStatusAsync(string accountId, AccountStatus status, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct);
		if (account is null)
			return new Error<string>("Account not found.");

		await database.UpdateAccountStatusAsync(accountId, status, ct);

		if (status is AccountStatus.Active)
			return new Success();

		// Status is persisted first, because it is the durable gate: every authenticated request
		// re-reads the account and rejects a non-Active one, so a token stops working at its next
		// request even if the revoke below fails. Revocation and ban enforcement are then attempted
		// independently — a failure in the first must not skip the second, which is what drops live
		// SignalR connections that never re-authenticate.
		try
		{
			await accountSessionStore.RevokeAllForAccountAsync(accountId, ct);
		}
		finally
		{
			if (banEnforcer is not null)
			{
				await banEnforcer.EnforceAccountBanAsync(accountId, ct);
			}
		}

		return new Success();
	}

	public ValueTask<OneOf<Success, Error<string>>> CloseAccountAsync(string accountId, CancellationToken ct = default)
		=> SetAccountStatusAsync(accountId, AccountStatus.Closed, ct);

	public ValueTask<OneOf<Success, Error<string>>> MarkAccountDeletedAsync(string accountId, CancellationToken ct = default)
		=> SetAccountStatusAsync(accountId, AccountStatus.Deleted, ct);
```

Then rewrite `DisableAccountAsync` and `EnableAccountAsync` to delegate, removing their now-duplicated bodies:

```csharp
	public ValueTask<OneOf<Success, Error<string>>> DisableAccountAsync(string accountId, CancellationToken ct = default)
		=> SetAccountStatusAsync(accountId, AccountStatus.Disabled, ct);

	public ValueTask<OneOf<Success, Error<string>>> EnableAccountAsync(string accountId, CancellationToken ct = default)
		=> SetAccountStatusAsync(accountId, AccountStatus.Active, ct);
```

The `try`/`finally` is the whole mitigation, deliberately. A retry queue or outbox would be
over-engineering here: `AccountSessionAuthenticationHandler` re-reads the account and checks
status on **every request**, so a failed revocation cannot leave a token usable — it only leaves
an already-established connection alive until enforcement runs. The `finally` closes that gap;
persisting status first means the durable gate is set before either side effect is attempted.

- [ ] **Step 5: Remove `DeleteAccountAsync` from the database layer**

Delete the declaration from `SharpMUSH.Library/ISharpDatabase.cs`, and delete the implementation from all three providers: `ArangoDatabase.Accounts.cs` (the method at ~line 116, including its character-link-removal query — severing those edges is exactly what this design stops doing), `MemgraphDatabase.Accounts.cs` (~line 97), `SurrealDatabase.Accounts.cs` (~line 111).

- [ ] **Step 6: Fix any test referencing the removed method**

Run: `dotnet build SharpMUSH.Tests`
Any test calling `DeleteAccountAsync` now fails to compile. Replace each call with `SetAccountStatusAsync(id, AccountStatus.Deleted)` and change its assertion from "row is gone" to "row exists with `Status == AccountStatus.Deleted`". If a test's entire purpose was asserting removal, rewrite it as a retention test:

```csharp
	[Test]
	public async ValueTask MarkDeleted_RetainsTheDocumentAndItsCredentials()
	{
		// <existing arrange: create an account via the real database fixture>
		await database.UpdateAccountStatusAsync(account.Id!, AccountStatus.Deleted);

		var reloaded = await database.GetAccountByIdAsync(account.Id!);

		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Deleted);
		await Assert.That(reloaded.Username).IsEqualTo(account.Username);
		await Assert.That(reloaded.PasswordHash).IsEqualTo(account.PasswordHash);
	}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountStatusTests/*"`
Then: `dotnet run --project SharpMUSH.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Library SharpMUSH.Database.ArangoDB SharpMUSH.Database.Memgraph SharpMUSH.Database.SurrealDB SharpMUSH.Tests
git commit -m "Make account close/delete a status transition; drop DeleteAccountAsync

Accounts are now never removed, so historical references to one always
resolve. Deleting also no longer severs edge_account_owns_character edges,
which would have erased the very history the status is there to preserve.

Disable/Enable/Close/MarkDeleted all delegate to SetAccountStatusAsync, so
session revocation happens in exactly one place."
```

---

### Task 3: The reserved system account

> **Partly landed already** — in PR #722, so this holds only once that has merged.
> `SharpMUSH.Library/Definitions/SystemAccount.cs` exists (`Username = "system"`,
> `IsReserved`), and `BootstrapService`'s first-run guard already asks "does any
> *non-reserved* account exist?" via `GetAllAccountsAsync` rather than `HasAnyAccountAsync`,
> with tests in `SharpMUSH.Tests/Services/BootstrapServiceTests.cs`.
>
> **What is left in this task**, precisely:
>
> - **Step 3 — skip.** `SystemAccount.cs` already exists exactly as written.
> - **Steps 4, 5, 7 — do.** The interface accessor, the two service guards, and
>   `GetOrCreateSystemAccountAsync`.
> - **Step 6 — do, reduced.** The guard is already correct; what is still missing is the
>   `GetOrCreateSystemAccountAsync` call at the top of `StartAsync`, so the account actually
>   gets created. Do not skip this — without it nothing ever creates the system account and
>   the guard change alone is inert.

**Files:**
- Create: `SharpMUSH.Library/Definitions/SystemAccount.cs` *(already exists)*
- Modify: `SharpMUSH.Library/Services/AccountService.cs` (`CreateAccountAsync`, `SetAccountStatusAsync`)
- Modify: `SharpMUSH.Library/Services/Interfaces/IAccountService.cs`
- Modify: `SharpMUSH.Server/Services/BootstrapService.cs:18-32`
- Test: `SharpMUSH.Tests/Services/AccountStatusTests.cs` (extend)

**Interfaces:**
- Consumes: `AccountStatus`, `SetAccountStatusAsync`, `CreateUnclaimedAccountAsync` from Tasks 1-2.
- Produces: `SystemAccount.Username` (`const string` = `"system"`); `IAccountService.GetOrCreateSystemAccountAsync(CancellationToken ct = default) -> ValueTask<SharpAccount>`. Phase 2 uses `GetOrCreateSystemAccountAsync` to attribute seeded wiki pages.

- [ ] **Step 1: Write the failing test**

Append to `SharpMUSH.Tests/Services/AccountStatusTests.cs`:

```csharp
	[Test]
	public async ValueTask SetAccountStatus_SystemAccount_ReturnsError()
	{
		var db = Substitute.For<ISharpDatabase>();
		var svc = new AccountService(db, Substitute.For<IPasswordService>(), Substitute.For<IAccountSessionStore>());

		var system = MakeAccount(AccountStatus.Active);
		system.Username = SystemAccount.Username;
		db.GetAccountByIdAsync("node_accounts/9", Arg.Any<CancellationToken>()).Returns(system);

		var result = await svc.SetAccountStatusAsync("node_accounts/9", AccountStatus.Closed);

		await Assert.That(result.IsT1).IsTrue();
		await db.DidNotReceive().UpdateAccountStatusAsync(Arg.Any<string>(), Arg.Any<AccountStatus>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask CreateAccount_ReservedSystemUsername_ReturnsError()
	{
		var db = Substitute.For<ISharpDatabase>();
		var svc = new AccountService(db, Substitute.For<IPasswordService>(), Substitute.For<IAccountSessionStore>());

		db.GetAccountByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SharpAccount?)null);
		db.GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SharpAccount?)null);

		var result = await svc.CreateAccountAsync(SystemAccount.Username.ToUpperInvariant(), null, "password123");

		await Assert.That(result.IsT1).IsTrue();
		await db.DidNotReceive().CreateAccountAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask GetOrCreateSystemAccount_IsIdempotent()
	{
		var db = Substitute.For<ISharpDatabase>();
		var svc = new AccountService(db, Substitute.For<IPasswordService>(), Substitute.For<IAccountSessionStore>());

		var existing = MakeAccount(AccountStatus.Active);
		existing.Username = SystemAccount.Username;
		db.GetAccountByUsernameAsync(SystemAccount.Username, Arg.Any<CancellationToken>()).Returns(existing);

		var result = await svc.GetOrCreateSystemAccountAsync();

		await Assert.That(result.Username).IsEqualTo(SystemAccount.Username);
		await db.DidNotReceive().CreateAccountAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
```

Add `using SharpMUSH.Library.Definitions;` to the file's usings.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountStatusTests/*"`
Expected: compile failure — `GetOrCreateSystemAccountAsync` is not defined on `IAccountService`. (`SystemAccount` itself resolves; it landed in PR #722.)

- [ ] **Step 3: Create the constant**

Create `SharpMUSH.Library/Definitions/SystemAccount.cs`:

```csharp
namespace SharpMUSH.Library.Definitions;

/// <summary>
/// The reserved account that owns server-authored content. Unreachable by construction: it is
/// created with an empty password hash (which never matches at the account level) and has no
/// linked characters, so no character password can authenticate it either.
/// </summary>
public static class SystemAccount
{
	public const string Username = "system";

	public static bool IsReserved(string username)
		=> string.Equals(username, Username, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Declare the accessor on the interface**

In `SharpMUSH.Library/Services/Interfaces/IAccountService.cs`, add:

```csharp
	/// <summary>
	/// Returns the reserved system account, creating it if absent. Idempotent — safe to call on
	/// every startup.
	/// </summary>
	ValueTask<SharpAccount> GetOrCreateSystemAccountAsync(CancellationToken ct = default);
```

- [ ] **Step 5: Implement the guards and the accessor**

In `SharpMUSH.Library/Services/AccountService.cs`, add at the top of `CreateAccountAsync`, before the username-taken check:

```csharp
		if (SystemAccount.IsReserved(username))
			return new Error<string>($"Username '{username}' is reserved.");
```

Add at the top of `SetAccountStatusAsync`, immediately after the not-found check:

```csharp
		if (SystemAccount.IsReserved(account.Username))
			return new Error<string>("The system account's status cannot be changed.");
```

Add the accessor:

```csharp
	public async ValueTask<SharpAccount> GetOrCreateSystemAccountAsync(CancellationToken ct = default)
		=> await database.GetAccountByUsernameAsync(SystemAccount.Username, ct)
			?? await CreateUnclaimedAccountAsync(SystemAccount.Username, ct);
```

Add `using SharpMUSH.Library.Definitions;` to the file's usings.

- [ ] **Step 6: Actually create the system account at bootstrap**

`BootstrapService.StartAsync` already carries the corrected guard but never creates the
account. Add the one missing call at the top, so the method reads:

```csharp
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await accountService.GetOrCreateSystemAccountAsync(cancellationToken);

		var accounts = await accountService.GetAllAccountsAsync(cancellationToken);
		if (accounts.Any(a => !SystemAccount.IsReserved(a.Username)))
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
```

Add `using SharpMUSH.Library.Definitions;` and `using System.Linq;` if not already present.

The `GetOrCreateSystemAccountAsync` line is the only new one; the guard beneath it landed in PR #722. `HasAnyAccountAsync` keeps its declaration — it is still a reasonable contract — but now has no production caller.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountStatusTests/*"`
Then: `dotnet run --project SharpMUSH.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Library SharpMUSH.Server SharpMUSH.Tests
git commit -m "Add the reserved system account

Owns server-authored content, and is unreachable by construction rather
than by a flag: empty password hash plus no linked characters. Its status
cannot be changed, since a closable system account would strand every
seeded page's attribution, and its username cannot be registered.

Bootstrap's first-run guard moves off HasAnyAccountAsync — the system
account now always exists, so 'any account exists' would be permanently
true and the admin account would never be pre-generated."
```

---

### Task 4: `@account/close` and `@account/delete`

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/AccountAdminCommands.cs:20-22,82-97`
- Modify: `SharpMUSH.Library/Definitions/ErrorMessages.cs` (the `Notifications` class, ~line 312)
- Modify: `SharpMUSH.Library/Resources/Notifications.resx`
- Modify: `SharpMUSH.Library/Resources/Notifications.fr.resx`
- Test: `SharpMUSH.Tests/Commands/AccountAdminCommandTests.cs`

**Interfaces:**
- Consumes: `CloseAccountAsync`, `MarkAccountDeletedAsync` from Task 2; `StatusLabel` from Task 1 Step 12.
- Produces: no new types.

- [ ] **Step 1: Write the failing test**

This class drives real services through a `ServerWebAppFactory`, not NSubstitute — it mirrors
`AccountDisable_BlocksLogin_EnableRestores`. Append to
`SharpMUSH.Tests/Commands/AccountAdminCommandTests.cs`:

```csharp
	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountClose_BlocksLoginAndRetainsTheRecord()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-close-user", "close@example.com", "some-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/close cmd-close-user"));
		await Task.Delay(200);

		await Assert.That(await accountService.AuthenticateAsync("cmd-close-user", "some-password-1")).IsNull();

		var reloaded = await accountService.GetByUsernameAsync("cmd-close-user");
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Closed);
		await Assert.That(reloaded.Email).IsEqualTo("close@example.com");
		await Assert.That(reloaded.PasswordHash).IsNotEmpty();
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountDelete_BlocksLoginAndRetainsTheRecord()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-delete-user", "delete@example.com", "some-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/delete cmd-delete-user"));
		await Task.Delay(200);

		await Assert.That(await accountService.AuthenticateAsync("cmd-delete-user", "some-password-1")).IsNull();

		var reloaded = await accountService.GetByUsernameAsync("cmd-delete-user");
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Deleted);
		await Assert.That(reloaded.Email).IsEqualTo("delete@example.com");
		await Assert.That(reloaded.PasswordHash).IsNotEmpty();
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountClose_SystemAccount_IsRefused()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var system = await accountService.GetOrCreateSystemAccountAsync();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@account/close {SystemAccount.Username}"));
		await Task.Delay(200);

		var reloaded = await accountService.GetByIdAsync(system.Id!);
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Active);
	}
```

The retention assertions are the point: these are the tests that prove an account leaving
`Active` keeps its username, email, and password hash.

Add `using SharpMUSH.Library.Definitions;` and `using SharpMUSH.Library.Models;` to the file's
usings.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountAdminCommandTests/*"`
Expected: FAIL — the switches are unrecognised, so the command falls through to the detail display, the account stays `Active`, and login still succeeds.

- [ ] **Step 3: Add the resource strings**

In `SharpMUSH.Library/Definitions/ErrorMessages.cs`, inside `public static class Notifications`, add:

```csharp
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AccountClosedFormat = "Account '{0}' closed; active sessions revoked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AccountMarkedDeletedFormat = "Account '{0}' marked deleted; active sessions revoked. The account record is retained.";
```

In `SharpMUSH.Library/Resources/Notifications.resx`, add:

```xml
  <data name="AccountClosedFormat" xml:space="preserve">
    <value>Account '{0}' closed; active sessions revoked.</value>
  </data>
  <data name="AccountMarkedDeletedFormat" xml:space="preserve">
    <value>Account '{0}' marked deleted; active sessions revoked. The account record is retained.</value>
  </data>
```

In `SharpMUSH.Library/Resources/Notifications.fr.resx`, add:

```xml
  <data name="AccountClosedFormat" xml:space="preserve">
    <value>Compte « {0} » fermé ; sessions actives révoquées.</value>
  </data>
  <data name="AccountMarkedDeletedFormat" xml:space="preserve">
    <value>Compte « {0} » marqué comme supprimé ; sessions actives révoquées. L'enregistrement du compte est conservé.</value>
  </data>
```

A missing `.fr` key falls back to the raw key rather than to English, so both files must be populated.

- [ ] **Step 4: Add the switches**

In `SharpMUSH.Implementation/Commands/AccountAdminCommands.cs`, extend the attribute's switch list:

```csharp
	[SharpCommand(Name = "@ACCOUNT", Switches = ["LIST", "NEWPASSWORD", "DISABLE", "ENABLE", "CLOSE", "DELETE"],
```

and update the doc comment's syntax line and the usage message to match:

```csharp
	/// <c>@account/disable &lt;name&gt;</c> / <c>@account/enable &lt;name&gt;</c>;
	/// <c>@account/close &lt;name&gt;</c> / <c>@account/delete &lt;name&gt;</c> — the account record is retained either way.</para>
```

```csharp
			await NotifyService!.Notify(executor, "Usage: @account[/list|/newpassword|/disable|/enable|/close|/delete] <name>[=<password>]");
```

Add the handlers immediately after the `ENABLE` block:

```csharp
		if (switches.Contains("CLOSE"))
		{
			var result = await AccountService.CloseAccountAsync(account.Id!);
			if (result.IsT0)
			{
				await NotifyService!.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AccountClosedFormat), executor, account.Username);
			}
			else
			{
				await NotifyService!.Notify(executor, result.AsT1.Value);
			}
			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			var result = await AccountService.MarkAccountDeletedAsync(account.Id!);
			if (result.IsT0)
			{
				await NotifyService!.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AccountMarkedDeletedFormat), executor, account.Username);
			}
			else
			{
				await NotifyService!.Notify(executor, result.AsT1.Value);
			}
			return CallState.Empty;
		}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AccountAdminCommandTests/*"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Implementation SharpMUSH.Library SharpMUSH.Tests
git commit -m "Add @account/close and @account/delete

Both are status transitions that retain the account record, so the
messages say so explicitly. Localized via Notifications.resx with the
matching .fr entries, since a missing key falls back to the raw key."
```

---

### Task 5: Admin status surface (API + portal)

**Files:**
- Modify: `SharpMUSH.Server/Controllers/AdminAccountsController.cs:94-115`
- Modify: `SharpMUSH.Client/Services/AdminAccountsService.cs`
- Modify: `SharpMUSH.Client/Pages/Admin/AdminAccounts.razor:28-95`
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx`
- Modify: `SharpMUSH.Client/Resources/SharedResource.fr.resx`
- Test: `SharpMUSH.Tests.BUnit/Pages/AdminAccountsPageTests.cs`
- Test: `SharpMUSH.Tests.Integration/Auth/AdminAccountsApiTests.cs`

**Interfaces:**
- Consumes: `AccountStatus`, `SetAccountStatusAsync`, `SystemAccount.IsReserved` from Tasks 1-3; `AdminAccountRow.Status` (string) from Task 1.
- Produces: `POST /api/admin/accounts/{key}/status` with body `{ "status": "Closed" }`; `AdminAccountsService.SetStatusAsync(string accountId, string status) -> Task<(bool Success, string? Error)>`.

- [ ] **Step 1: Write the failing test**

Two changes in `SharpMUSH.Tests.BUnit/Pages/AdminAccountsPageTests.cs`.

First, extend the `file sealed class AdminAccountsApiHandler` fixture. Add `system` and
`Closed`/`Deleted` rows to its `rows` array (keeping the two existing rows, whose `IsDisabled`
became `Status` in Task 1):

```csharp
								new
								{
										Id = "3",
										Username = "departed-account",
										Email = (string?)null,
										Status = "Closed",
										MustChangePassword = false,
										Characters = Array.Empty<object>(),
								},
								new
								{
										Id = "4",
										Username = "erased-account",
										Email = (string?)null,
										Status = "Deleted",
										MustChangePassword = false,
										Characters = Array.Empty<object>(),
								},
								new
								{
										Id = "9",
										Username = "system",
										Email = (string?)null,
										Status = "Active",
										MustChangePassword = false,
										Characters = Array.Empty<object>(),
								},
```

and add `/status` to the POST path allowlist:

```csharp
		if (request.Method == HttpMethod.Post &&
				(path.EndsWith("/reset-password") || path.EndsWith("/disable") || path.EndsWith("/enable")
					|| path.EndsWith("/status")))
```

Second, append the tests:

```csharp
	[TUnit.Core.Test]
	public async Task RendersEveryStatus_ForAuthorizedUser()
	{
		Auth.SetAuthorized("headwiz");
		Auth.SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.Admin.AdminAccounts>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("departed-account"))
				throw new InvalidOperationException("account rows not rendered yet");
		});

		await Assert.That(cut.Markup).Contains("AccountStatusActive");
		await Assert.That(cut.Markup).Contains("AccountStatusDisabled");
		await Assert.That(cut.Markup).Contains("AccountStatusClosed");
		await Assert.That(cut.Markup).Contains("AccountStatusDeleted");
	}

	[TUnit.Core.Test]
	public async Task SystemAccountRow_OffersNoStatusActions()
	{
		Auth.SetAuthorized("headwiz");
		Auth.SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.Admin.AdminAccounts>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("system"))
				throw new InvalidOperationException("account rows not rendered yet");
		});

		// Five rows, four of them non-system, each offering the targets for its status:
		// Active -> 3, Disabled -> 1, Closed -> 1, Deleted -> 1. The system row adds none.
		var actions = cut.FindAll("button[data-testid='account-status-action']");
		await Assert.That(actions.Count).IsEqualTo(6);
	}
```

The assertions look for resource *keys*, not English: every bUnit test in this repo stubs the
localizer with a key-echoing double, so `Loc["AccountStatusClosed"]` renders as
`AccountStatusClosed`.

**This breaks an existing assertion.** `RendersAccountRows_ForAuthorizedUser` asserts
`cut.Markup).Contains("DISABLED")` twice (once inside its `WaitForAssertion`), against the literal
the status cell renders today. Step 5 replaces that literal with a localized chip, so change both
occurrences to `"AccountStatusDisabled"`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AdminAccountsPageTests/*"`
Expected: FAIL — no `data-testid='account-status-action'` attribute exists, and only Active/Disabled are rendered.

- [ ] **Step 3: Add the API endpoint**

In `SharpMUSH.Server/Controllers/AdminAccountsController.cs`, add after the existing `Enable` action:

```csharp
	public record SetStatusRequest(string Status);

	[HttpPost("{key}/status")]
	public async Task<IActionResult> SetStatus(string key, [FromBody] SetStatusRequest request)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;

		if (!Enum.TryParse<AccountStatus>(request.Status, ignoreCase: true, out var status))
			return BadRequest($"Unknown account status '{request.Status}'.");

		var result = await accountService.SetAccountStatusAsync(FullId(key), status);
		if (result.IsT1) return NotFound(result.AsT1.Value);

		logger.LogInformation("Admin {AdminId} set account {Key} status to {Status}",
			LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key), status);
		return NoContent();
	}
```

This matches the existing `Disable`/`Enable` actions exactly: `RequireWizardAsync()` for the
guard, `NotFound` on the service's error case, `NoContent()` on success. Add
`using SharpMUSH.Library.Models;` if not already present.

Leave the existing `disable` and `enable` endpoints in place; they are still the two-click path and now both funnel into the same service method.

Note the one asymmetry: an unknown status string is a `BadRequest`, while a missing account or the
system-account guard is a `NotFound` — because that is what the existing actions return for the
service's error case, and consistency with its neighbours matters more here than a perfectly
chosen code.

- [ ] **Step 4: Add the client service call**

In `SharpMUSH.Client/Services/AdminAccountsService.cs`, add alongside `SetDisabledAsync`, mirroring its error-handling shape:

```csharp
	public async Task<(bool Success, string? Error)> SetStatusAsync(string accountId, string status)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		var response = await http.PostAsJsonAsync($"api/admin/accounts/{key}/status", new { status });
		return response.IsSuccessStatusCode
			? (true, null)
			: (false, await response.Content.ReadAsStringAsync());
	}
```

- [ ] **Step 5: Replace the toggle with a status control**

In `SharpMUSH.Client/Pages/Admin/AdminAccounts.razor`, replace the status cell (line ~32) and the action cell (lines ~56-58).

Status cell:

```razor
                <MudChip T="string" Size="Size.Small" Color="@StatusColor(context.Status)">
                    @Loc[$"AccountStatus{context.Status}"]
                </MudChip>
```

Action cell:

```razor
                @if (!IsSystemAccount(context))
                {
                    @foreach (var target in StatusTargets(context.Status))
                    {
                        <MudButton Size="Size.Small" data-testid="account-status-action"
                                   OnClick="@(() => SetStatus(context, target))">
                            @Loc[$"AccountAction{target}"]
                        </MudButton>
                    }
                }
```

In the `@code` block, replace `ToggleDisabled` with:

```csharp
    private static bool IsSystemAccount(AdminAccountRow row) =>
        string.Equals(row.Username, "system", StringComparison.OrdinalIgnoreCase);

    private static Color StatusColor(string status) => status switch
    {
        "Active" => Color.Success,
        "Disabled" => Color.Warning,
        _ => Color.Error
    };

    private static string[] StatusTargets(string status) => status switch
    {
        "Active" => ["Disabled", "Closed", "Deleted"],
        _ => ["Active"]
    };

    private async Task SetStatus(AdminAccountRow row, string target)
    {
        var (success, error) = await AccountsService.SetStatusAsync(row.Id, target);
        var message = success
            ? Loc["AccountStatusChanged"]
            : error ?? Loc["AccountStatusChangeFailed"];
        Snackbar.Add(message, success ? Severity.Success : Severity.Error);
        if (success)
        {
            await LoadAsync();
        }
    }
```

Match the existing file's snackbar and reload calls — `Snackbar.Add` and `LoadAsync()` above stand in for whatever it already uses.

- [ ] **Step 6: Add the portal strings**

In `SharpMUSH.Client/Resources/SharedResource.resx`:

```xml
  <data name="AccountStatusActive" xml:space="preserve"><value>Active</value></data>
  <data name="AccountStatusDisabled" xml:space="preserve"><value>Disabled</value></data>
  <data name="AccountStatusClosed" xml:space="preserve"><value>Closed</value></data>
  <data name="AccountStatusDeleted" xml:space="preserve"><value>Deleted</value></data>
  <data name="AccountActionActive" xml:space="preserve"><value>Reactivate</value></data>
  <data name="AccountActionDisabled" xml:space="preserve"><value>Disable</value></data>
  <data name="AccountActionClosed" xml:space="preserve"><value>Close</value></data>
  <data name="AccountActionDeleted" xml:space="preserve"><value>Delete</value></data>
  <data name="AccountStatusChanged" xml:space="preserve"><value>Account status updated.</value></data>
  <data name="AccountStatusChangeFailed" xml:space="preserve"><value>Could not update the account status.</value></data>
```

In `SharpMUSH.Client/Resources/SharedResource.fr.resx`:

```xml
  <data name="AccountStatusActive" xml:space="preserve"><value>Actif</value></data>
  <data name="AccountStatusDisabled" xml:space="preserve"><value>Désactivé</value></data>
  <data name="AccountStatusClosed" xml:space="preserve"><value>Fermé</value></data>
  <data name="AccountStatusDeleted" xml:space="preserve"><value>Supprimé</value></data>
  <data name="AccountActionActive" xml:space="preserve"><value>Réactiver</value></data>
  <data name="AccountActionDisabled" xml:space="preserve"><value>Désactiver</value></data>
  <data name="AccountActionClosed" xml:space="preserve"><value>Fermer</value></data>
  <data name="AccountActionDeleted" xml:space="preserve"><value>Supprimer</value></data>
  <data name="AccountStatusChanged" xml:space="preserve"><value>Statut du compte mis à jour.</value></data>
  <data name="AccountStatusChangeFailed" xml:space="preserve"><value>Impossible de mettre à jour le statut du compte.</value></data>
```

- [ ] **Step 7: Add the API integration test**

Append to `SharpMUSH.Tests.Integration/Auth/AdminAccountsApiTests.cs`. This class is
`[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]` and builds clients via
its own `CreateClient()`; read `GodAccount_CanListAndResetPassword` and copy its
`NotInParallel("SetupFlow", Order = N)` attribute with a **new, distinct `Order`** — the class doc
comment explains that distinct `Order` values across the whole `"SetupFlow"` group are required for
determinism, so do not reuse an existing one. Copy its login-and-authorize sequence verbatim for
obtaining an admin-bearer client.

```csharp
	[Test, NotInParallel("SetupFlow", Order = 7)]
	public async ValueTask SetStatus_Closed_RetainsTheRowWithTheNewStatus()
	{
		var http = CreateClient();
		// <copy the LoginAsGodAccountAsync + AuthenticationHeaderValue setup from
		//  GodAccount_CanListAndResetPassword here>

		await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest("status-target", null, Password));

		var rows = await http.GetFromJsonAsync<List<AdminAccountRow>>("api/admin/accounts");
		var key = rows!.Single(r => r.Username == "status-target").Id.Split('/')[^1];

		var response = await http.PostAsJsonAsync($"api/admin/accounts/{key}/status", new { status = "Closed" });
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var after = await http.GetFromJsonAsync<List<AdminAccountRow>>("api/admin/accounts");
		var row = after!.Single(r => r.Username == "status-target");
		await Assert.That(row.Status).IsEqualTo("Closed");

		var login = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest("status-target", Password));
		await Assert.That(login.IsSuccessStatusCode).IsFalse();
	}

	[Test, NotInParallel("SetupFlow", Order = 8)]
	public async ValueTask SetStatus_UnknownStatus_ReturnsBadRequest()
	{
		var http = CreateClient();
		// <same admin-bearer setup as above>

		await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest("status-bogus", null, Password));

		var rows = await http.GetFromJsonAsync<List<AdminAccountRow>>("api/admin/accounts");
		var key = rows!.Single(r => r.Username == "status-bogus").Id.Split('/')[^1];

		var response = await http.PostAsJsonAsync($"api/admin/accounts/{key}/status", new { status = "Banished" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}
```

The final login assertion in the first test is the one that matters end-to-end: it proves the
status transition actually gates authentication through the real HTTP surface, not just the row
projection.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AdminAccountsPageTests/*"`
Then: `dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/AdminAccountsApiTests/*"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add SharpMUSH.Server SharpMUSH.Client SharpMUSH.Tests.BUnit SharpMUSH.Tests.Integration
git commit -m "Add a four-state account status control to the admin portal

One POST /status endpoint replaces the disable/enable pair as the general
path; the old endpoints stay and now funnel into the same service method.
The system account's row offers no actions, matching the server-side guard
rather than relying on it alone. Labels come from SharedResource.resx with
.fr entries populated."
```

---

### Tasks 6 and 7: superseded — already implemented

**Do not implement these.** They were planned here and then done as a separate change,
because the objid contract is a cross-cutting transport concern rather than attribution
work. What shipped differs from what this plan described, so the original steps would be
actively misleading:

- There is no `CharacterIdentity` helper. The accessor is
  `CharacterClaimsExtensions.GetActingCharacter(this ClaimsPrincipal) -> DBRef?`, which
  already existed on `main` returning a raw string and now returns a parsed `DBRef`.
- `GameHub.CharacterGroupName`, `RoomGroupName`, `SendToCharacterAsync`, and
  `SendToRoomAsync` take a `DBRef` rather than a string, so the group-name hazard this
  plan flagged is closed by the type rather than by normalization.
- All three handlers emit `character_dbref` as `player.Object.DBRef.ToString()`.
- `MailController` and `GalleryController` use the parsed accessor; both had been
  hand-stripping the objid suffix and rebuilding `new DBRef(n, null)`.
- `GameOutputMessage.CharacterDbref` is nullable; `null` means a server-wide broadcast,
  replacing the `"*"` sentinel.

Task 8 below is the one piece of identity plumbing still outstanding.

---


### Task 8: `NameIdentifier` becomes uniformly the account id

This is last on purpose: it removes the last producer of a dbref-shaped `NameIdentifier`, so every consumer must already be off it. The controllers that read it as a character reference now go through `GetActingCharacter` instead, so nothing is left depending on the old shape.

**Files:**
- Modify: `SharpMUSH.Server/Authentication/MushBasicAuthenticationHandler.cs:113-122`
- Test: `SharpMUSH.Tests/Authentication/` — the existing basic-auth handler test class, or create `MushBasicAuthHandlerClaimsTests.cs`

**Interfaces:**
- Consumes: `IAccountService.GetAccountForCharacterAsync` (pre-existing); `CharacterClaimsExtensions.GetActingCharacter` (already implemented).
- Produces: no new types. Establishes the invariant that `ClaimTypes.NameIdentifier` is an account id on every principal, which phase 2's `WikiController.CallerAccountId` depends on.

- [ ] **Step 1: Write the failing test**

```csharp
	[Test]
	public async ValueTask Authenticate_CharacterWithAccount_PutsAccountIdInNameIdentifier()
	{
		// Arrange as the existing successful-auth test does, plus:
		AccountService.GetAccountForCharacterAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(new SharpAccount { Id = "node_accounts/7", Username = "owner", PasswordHash = "h" });

		var result = await AuthenticateAsync("TestChar", "correct-password");

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)).IsEqualTo("node_accounts/7");
		await Assert.That(result.Principal!.GetActingCharacter()!.Value.Number).IsEqualTo(TestCharacterKey);
	}

	[Test]
	public async ValueTask Authenticate_CharacterWithoutAccount_SucceedsWithNoNameIdentifier()
	{
		AccountService.GetAccountForCharacterAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await AuthenticateAsync("TestChar", "correct-password");

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)).IsNull();
		await Assert.That(result.Principal!.GetActingCharacter()!.Value.Number).IsEqualTo(TestCharacterKey);
	}
```

`AuthenticateAsync`, `AccountService`, and `TestCharacterKey` stand in for the existing fixture in that test class. If the handler does not currently take an `IAccountService`, the test will not compile until Step 3 adds it.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MushBasicAuth*/*"`
Expected: FAIL — `NameIdentifier` is `"#<key>"`, not an account id.

- [ ] **Step 3: Resolve the owning account in the handler**

In `SharpMUSH.Server/Authentication/MushBasicAuthenticationHandler.cs`, add `IAccountService accountService` to the primary constructor parameter list, then replace the claims block:

```csharp
		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, player.Object.Name),
			new("character_key", player.Object.Key.ToString()),
			new("character_creation_time", player.Object.CreationTime.ToString()),
			new("character_name", player.Object.Name),
			new(GameHub.CharacterDbrefClaim, player.Object.DBRef.ToString())
		};

		// NameIdentifier is the ACCOUNT id on every principal. A character with no owning account
		// still authenticates here — this is character-password basic auth — but carries no account
		// id, so account-anchored writes reject it.
		var owningAccount = await accountService.GetAccountForCharacterAsync(player.Object.DBRef);
		if (owningAccount?.Id is { } accountId)
		{
			claims.Add(new Claim(ClaimTypes.NameIdentifier, accountId));
		}
```

Verify the handler's DI registration resolves `IAccountService` — `AuthenticationHandler` subclasses are constructed from the request scope, and `IAccountService` is already scoped, so no registration change should be needed. If the build complains, register it where the scheme is added in `SharpMUSH.Server`.

- [ ] **Step 4: Update the doc comment on `ApiControllerBase.CurrentAccountId`**

`SharpMUSH.Server/Controllers/ApiControllerBase.cs:19-21` describes `NameIdentifier` as "(account GUID)". It is now accurate for every scheme, but the wording is misleading — account ids are `node_accounts/<key>`, not GUIDs:

```csharp
    /// <summary>
    /// The account id from the request principal ("node_accounts/&lt;key&gt;"), or
    /// <see langword="null"/> when the principal carries none. Every authentication handler puts
    /// the account id here; the acting character lives in its own claims — see
    /// <see cref="Authentication.CharacterClaimsExtensions"/>.
    /// </summary>
    protected string? CurrentAccountId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier);
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet run --project SharpMUSH.Tests`
Then: `dotnet run --project SharpMUSH.Tests.Integration`
Then: `dotnet run --project SharpMUSH.Tests.BUnit`
Expected: PASS. Any remaining failure is a consumer still reading `NameIdentifier` as a dbref — fix it with `GetActingCharacter`.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Server SharpMUSH.Tests
git commit -m "Make NameIdentifier the account id on every principal

MushBasicAuthenticationHandler was the last producer of a dbref-shaped
NameIdentifier, which is why ApiControllerBase.CurrentAccountId's doc
comment was false for that scheme. It now resolves the character's owning
account; a character with no account still authenticates but carries no
account id, so account-anchored writes reject it."
```

---

## Verification

After Task 8, all of these must hold. Run them as a block before declaring the phase done:

```bash
dotnet build
dotnet run --project SharpMUSH.Tests
dotnet run --project SharpMUSH.Tests.BUnit
dotnet run --project SharpMUSH.Tests.Integration
```

Manual check, since it exercises bootstrap ordering that no test covers end to end:

1. Delete the database (`podman compose down -v && podman compose up -d` — this environment has Podman with `DOCKER_HOST` set and no `docker` binary — or drop the Arango database directly).
2. Start `SharpMUSH.Server`.
3. Confirm the log line `Bootstrap: pre-generated unclaimed admin account linked to #1.` appears — this proves the system account's existence did not suppress admin pre-generation. `BootstrapServiceTests` covers the guard in isolation; this checks it end to end against a real database.
4. Run `@account/list` in-game as God and confirm both `system` and `admin` appear, `system` showing `active`.
5. Run `@account/close system` and confirm it is refused.

## Out of scope for this plan

Phase 2, specified in the same design document: the `edge_account_contributed` collection, wiki page and revision attribution, asset metadata moving into the database, and the wiki DTO/UI changes. None of it is started here.
