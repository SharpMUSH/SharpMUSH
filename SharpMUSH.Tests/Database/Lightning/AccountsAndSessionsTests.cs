using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

public class AccountsAndSessionsTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>());

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public void Cleanup()
	{
		_db.Store.Dispose();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
				// join by a few milliseconds under load. Leaving the temp directory behind costs
				// disk, not correctness — matches MigrationTests' own cleanup.
			}
		}
	}

	/// <summary>Strips the "node_accounts/" prefix off a <see cref="SharpAccount.Id"/> to get the raw key
	/// that <c>Tables.AccountEmail</c>/<c>Tables.AccountUser</c> index values store.</summary>
	private static string RawKey(string accountId) => accountId[(accountId.IndexOf('/') + 1)..];

	private static SharpSession MakeSession(string token, string accountId, string originIp) => new()
	{
		Token = token,
		AccountId = accountId,
		OriginIp = originIp,
		ExpiryUnixMs = 1_000,
		TtlMs = 500
	};

	[Test]
	public async Task CreateAccountAsyncIsFoundByEmailCaseInsensitiveAndByUsername()
	{
		var created = await _db.CreateAccountAsync("Alice", "Alice@Example.com", "hash");

		var byEmail = await _db.GetAccountByEmailAsync("alice@example.com");
		var byUsername = await _db.GetAccountByUsernameAsync("Alice");
		var byId = await _db.GetAccountByIdAsync(created.Id!);

		await Assert.That(byEmail).IsNotNull();
		await Assert.That(byEmail!.Id).IsEqualTo(created.Id);
		await Assert.That(byUsername).IsNotNull();
		await Assert.That(byUsername!.Id).IsEqualTo(created.Id);
		await Assert.That(byId).IsNotNull();
		await Assert.That(byId!.Username).IsEqualTo("Alice");
	}

	[Test]
	public async Task CreateAccountAsyncRefusesADuplicateEmail()
	{
		await _db.CreateAccountAsync("Alice", "shared@example.com", "hash");

		await Assert.ThrowsAsync<InvalidOperationException>(async ()
			=> await _db.CreateAccountAsync("Bob", "shared@example.com", "hash2"));
	}

	[Test]
	public async Task CreateAccountAsyncRefusesADuplicateUsername()
	{
		await _db.CreateAccountAsync("Alice", null, "hash");

		await Assert.ThrowsAsync<InvalidOperationException>(async ()
			=> await _db.CreateAccountAsync("Alice", "other@example.com", "hash2"));
	}

	[Test]
	public async Task HasAnyAccountAsyncReflectsWhetherAnAccountExists()
	{
		await Assert.That(await _db.HasAnyAccountAsync()).IsFalse();

		await _db.CreateAccountAsync("Alice", null, "hash");

		await Assert.That(await _db.HasAnyAccountAsync()).IsTrue();
	}

	[Test]
	public async Task LinkAndUnlinkCharacterToAccountRoundTripsBothDirections()
	{
		var account = await _db.CreateAccountAsync("Alice", null, "hash");
		var god = new DBRef(1);

		await _db.LinkCharacterToAccountAsync(account.Id!, god);

		var characters = await _db.GetCharactersForAccountAsync(account.Id!);
		var owner = await _db.GetAccountForCharacterAsync(god);

		await Assert.That(characters.Select(c => c.Object.Key)).Contains(1);
		await Assert.That(owner).IsNotNull();
		await Assert.That(owner!.Id).IsEqualTo(account.Id);

		await _db.UnlinkCharacterFromAccountAsync(account.Id!, god);

		var charactersAfterUnlink = await _db.GetCharactersForAccountAsync(account.Id!);
		var ownerAfterUnlink = await _db.GetAccountForCharacterAsync(god);

		await Assert.That(charactersAfterUnlink).IsEmpty();
		await Assert.That(ownerAfterUnlink).IsNull();
	}

	[Test]
	public async Task UpdateAccountEmailAsyncClearingEmailRemovesTheOldIndexEntryAndAddsNone()
	{
		var account = await _db.CreateAccountAsync("Carol", "carol@example.com", "hash");

		await _db.UpdateAccountEmailAsync(account.Id!, null);

		var oldIndexed = _db.Store.Read(tx => tx.TryGet(Tables.AccountEmail, Keys.Lower("carol@example.com"), out _));
		await Assert.That(oldIndexed).IsFalse();
		await Assert.That(await _db.GetAccountByEmailAsync("carol@example.com")).IsNull();

		var byId = await _db.GetAccountByIdAsync(account.Id!);
		await Assert.That(byId!.Email).IsNull();
	}

	[Test]
	public async Task UpdateAccountEmailAsyncReassigningEmailMovesTheIndexEntry()
	{
		var account = await _db.CreateAccountAsync("Dave", "dave-old@example.com", "hash");

		await _db.UpdateAccountEmailAsync(account.Id!, "dave-new@example.com");

		var oldIndexed = _db.Store.Read(tx => tx.TryGet(Tables.AccountEmail, Keys.Lower("dave-old@example.com"), out _));
		var newIndexed = _db.Store.Read(tx => tx.TryGet(Tables.AccountEmail, Keys.Lower("dave-new@example.com"), out _));
		await Assert.That(oldIndexed).IsFalse();
		await Assert.That(newIndexed).IsTrue();

		await Assert.That(await _db.GetAccountByEmailAsync("dave-old@example.com")).IsNull();
		var byNewEmail = await _db.GetAccountByEmailAsync("dave-new@example.com");
		await Assert.That(byNewEmail).IsNotNull();
		await Assert.That(byNewEmail!.Id).IsEqualTo(account.Id);
	}

	[Test]
	public async Task UpdateAccountUsernameAsyncRenamingMovesTheIndexEntry()
	{
		var account = await _db.CreateAccountAsync("Erin", null, "hash");

		await _db.UpdateAccountUsernameAsync(account.Id!, "ErinRenamed");

		var oldIndexed = _db.Store.Read(tx => tx.TryGet(Tables.AccountUser, Keys.Lower("Erin"), out _));
		var newIndexed = _db.Store.Read(tx => tx.TryGet(Tables.AccountUser, Keys.Lower("ErinRenamed"), out _));
		await Assert.That(oldIndexed).IsFalse();
		await Assert.That(newIndexed).IsTrue();

		await Assert.That(await _db.GetAccountByUsernameAsync("Erin")).IsNull();
		var byNewUsername = await _db.GetAccountByUsernameAsync("ErinRenamed");
		await Assert.That(byNewUsername).IsNotNull();
		await Assert.That(byNewUsername!.Id).IsEqualTo(account.Id);
	}

	[Test]
	public async Task UpdateAccountEmailAsyncRenamingToAnotherAccountsEmailThrowsAndLeavesBothAccountsUnchanged()
	{
		var owner = await _db.CreateAccountAsync("Frank", "frank@example.com", "hash-frank");
		var renamer = await _db.CreateAccountAsync("Grace", "grace@example.com", "hash-grace");

		await Assert.ThrowsAsync<InvalidOperationException>(async ()
			=> await _db.UpdateAccountEmailAsync(renamer.Id!, "frank@example.com"));

		var ownerAfter = await _db.GetAccountByIdAsync(owner.Id!);
		var renamerAfter = await _db.GetAccountByIdAsync(renamer.Id!);
		await Assert.That(ownerAfter!.Email).IsEqualTo("frank@example.com");
		await Assert.That(renamerAfter!.Email).IsEqualTo("grace@example.com");

		var frankIndexedOwner = _db.Store.Read(tx => tx.TryGet(Tables.AccountEmail, Keys.Lower("frank@example.com"), out var v) ? Keys.ReadStr(v) : null);
		var graceIndexedOwner = _db.Store.Read(tx => tx.TryGet(Tables.AccountEmail, Keys.Lower("grace@example.com"), out var v) ? Keys.ReadStr(v) : null);
		await Assert.That(frankIndexedOwner).IsEqualTo(RawKey(owner.Id!));
		await Assert.That(graceIndexedOwner).IsEqualTo(RawKey(renamer.Id!));
	}

	[Test]
	public async Task UpdateAccountUsernameAsyncRenamingToAnotherAccountsUsernameThrowsAndLeavesBothAccountsUnchanged()
	{
		var owner = await _db.CreateAccountAsync("Hank", null, "hash-hank");
		var renamer = await _db.CreateAccountAsync("Irene", null, "hash-irene");

		await Assert.ThrowsAsync<InvalidOperationException>(async ()
			=> await _db.UpdateAccountUsernameAsync(renamer.Id!, "Hank"));

		var ownerAfter = await _db.GetAccountByIdAsync(owner.Id!);
		var renamerAfter = await _db.GetAccountByIdAsync(renamer.Id!);
		await Assert.That(ownerAfter!.Username).IsEqualTo("Hank");
		await Assert.That(renamerAfter!.Username).IsEqualTo("Irene");

		var hankIndexedOwner = _db.Store.Read(tx => tx.TryGet(Tables.AccountUser, Keys.Lower("Hank"), out var v) ? Keys.ReadStr(v) : null);
		var ireneIndexedOwner = _db.Store.Read(tx => tx.TryGet(Tables.AccountUser, Keys.Lower("Irene"), out var v) ? Keys.ReadStr(v) : null);
		await Assert.That(hankIndexedOwner).IsEqualTo(RawKey(owner.Id!));
		await Assert.That(ireneIndexedOwner).IsEqualTo(RawKey(renamer.Id!));
	}

	[Test]
	public async Task UpdateAccountStatusAsyncRoundTrips()
	{
		var account = await _db.CreateAccountAsync("Judy", null, "hash");

		await _db.UpdateAccountStatusAsync(account.Id!, AccountStatus.Disabled);

		var byId = await _db.GetAccountByIdAsync(account.Id!);
		await Assert.That(byId!.Status).IsEqualTo(AccountStatus.Disabled);
	}

	[Test]
	public async Task UpdateAccountPasswordAsyncRoundTrips()
	{
		var account = await _db.CreateAccountAsync("Karl", null, "old-hash");

		await _db.UpdateAccountPasswordAsync(account.Id!, "new-hash");

		var byId = await _db.GetAccountByIdAsync(account.Id!);
		await Assert.That(byId!.PasswordHash).IsEqualTo("new-hash");
	}

	[Test]
	public async Task UpdateAccountMustChangePasswordAsyncRoundTrips()
	{
		var account = await _db.CreateAccountAsync("Liam", null, "hash");
		await Assert.That(account.MustChangePassword).IsFalse();

		await _db.UpdateAccountMustChangePasswordAsync(account.Id!, true);

		var byId = await _db.GetAccountByIdAsync(account.Id!);
		await Assert.That(byId!.MustChangePassword).IsTrue();
	}

	[Test]
	public async Task GetAllAccountsAsyncOrdersUsernamesOrdinallyWithUppercaseBeforeLowercase()
	{
		// StringComparer.Ordinal sorts by UTF-16 code unit, so 'B' (0x42) sorts before 'a' (0x61):
		// this asserts the exact order LightningDatabase.GetAllAccountsAsync produces, not a
		// case-insensitive alphabetical order.
		await _db.CreateAccountAsync("charlie", null, "hash");
		await _db.CreateAccountAsync("adam", null, "hash");
		await _db.CreateAccountAsync("Bob", null, "hash");

		var all = await _db.GetAllAccountsAsync();

		await Assert.That(all.Select(a => a.Username)).IsEquivalentTo(["Bob", "adam", "charlie"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task UpsertSessionAsyncRoundTrips()
	{
		var session = MakeSession("tok-1", "node_accounts/1", "203.0.113.5");

		await _db.UpsertSessionAsync(session);
		var got = await _db.GetSessionAsync("tok-1");

		await Assert.That(got).IsNotNull();
		await Assert.That(got!.AccountId).IsEqualTo("node_accounts/1");
		await Assert.That(got.OriginIp).IsEqualTo("203.0.113.5");
	}

	[Test]
	public async Task TouchSessionExpiryAsyncExtendsAnExistingSession()
	{
		await _db.UpsertSessionAsync(MakeSession("tok-2", "node_accounts/1", "203.0.113.5"));

		var touched = await _db.TouchSessionExpiryAsync("tok-2", 9_999);
		var got = await _db.GetSessionAsync("tok-2");

		await Assert.That(touched).IsTrue();
		await Assert.That(got!.ExpiryUnixMs).IsEqualTo(9_999);
	}

	[Test]
	public async Task TouchSessionExpiryAsyncOfAnAbsentTokenIsANoOp()
	{
		var touched = await _db.TouchSessionExpiryAsync("never-existed", 9_999);
		var got = await _db.GetSessionAsync("never-existed");

		await Assert.That(touched).IsFalse();
		await Assert.That(got).IsNull();
	}

	[Test]
	public async Task DeleteSessionsForAccountAsyncRemovesAllOfThatAccountsSessionsAndSecondaries()
	{
		await _db.UpsertSessionAsync(MakeSession("tok-3", "node_accounts/1", "203.0.113.5"));
		await _db.UpsertSessionAsync(MakeSession("tok-4", "node_accounts/1", "203.0.113.6"));
		await _db.UpsertSessionAsync(MakeSession("tok-5", "node_accounts/2", "203.0.113.7"));

		await _db.DeleteSessionsForAccountAsync("node_accounts/1");

		await Assert.That(await _db.GetSessionAsync("tok-3")).IsNull();
		await Assert.That(await _db.GetSessionAsync("tok-4")).IsNull();
		await Assert.That(await _db.GetSessionAsync("tok-5")).IsNotNull();

		var remainingIps = await _db.GetSessionOriginIpsAsync();
		await Assert.That(remainingIps).Contains("203.0.113.7");
		await Assert.That(remainingIps).DoesNotContain("203.0.113.5");
		await Assert.That(remainingIps).DoesNotContain("203.0.113.6");
	}

	[Test]
	public async Task GetSessionOriginIpsAsyncReturnsDistinctIps()
	{
		await _db.UpsertSessionAsync(MakeSession("tok-6", "node_accounts/1", "203.0.113.9"));
		await _db.UpsertSessionAsync(MakeSession("tok-7", "node_accounts/2", "203.0.113.9"));
		await _db.UpsertSessionAsync(MakeSession("tok-8", "node_accounts/3", "203.0.113.10"));

		var ips = await _db.GetSessionOriginIpsAsync();

		await Assert.That(ips.Length).IsEqualTo(2);
		await Assert.That(ips).Contains("203.0.113.9");
		await Assert.That(ips).Contains("203.0.113.10");
	}
}
