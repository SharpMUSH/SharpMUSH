using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

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
