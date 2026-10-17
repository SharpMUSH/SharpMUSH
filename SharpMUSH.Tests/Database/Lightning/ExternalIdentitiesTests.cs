using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class ExternalIdentitiesTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private static ExternalIdentityKey Key(string subject = "subject", string registration = "google") => new(registration, "https://issuer.example", subject);

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "external-identities-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		await FixtureDirectoryCleanup.DeleteAsync(_path);
	}

	[Test]
	public async Task LinkingTheSameOwnedIdentityIsIdempotentWithoutAdvancingCredentials()
	{
		var account = await _db.CreateAccountAsync("Idempotent", null, "hash");
		var first = (await _db.AttachExternalIdentityAsync(account.Id!, Key(), "Original")).Expect<ExternalIdentity>();
		var epoch = (await _db.GetAccountByIdAsync(account.Id!))!.CredentialEpoch;
		var repeated = (await _db.AttachExternalIdentityAsync(account.Id!, Key(), "Different display")).Expect<ExternalIdentity>();
		await Assert.That(repeated).IsEqualTo(first);
		await Assert.That((await _db.GetAccountByIdAsync(account.Id!))!.CredentialEpoch).IsEqualTo(epoch);
	}

	[Test]
	public async Task MigrationPreservesOrdinaryAccountHashStatusLinksSessionsAndExplicitSocialPolicy()
	{
		var ordinary = await _db.CreateAccountAsync("Ordinary", null, "unchanged-hash");
		await _db.UpdateAccountMustChangePasswordAsync(ordinary.Id!, true);
		await _db.LinkCharacterToAccountAsync(ordinary.Id!, new DBRef(1));
		await _db.Store.WriteAsync(tx => tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.AccountCredentialMigrationId)));
		await _db.Migrate();
		var social = (await _db.ProvisionExternalAccountAsync(Key(), "Social", null)).Expect<SharpAccount>();
		await _db.Migrate();
		await _db.Migrate();
		var stored = (await _db.GetAccountByIdAsync(ordinary.Id!))!;
		await Assert.That(stored.PasswordHash).IsEqualTo("unchanged-hash");
		await Assert.That(stored.MustChangePassword).IsTrue();
		await Assert.That(stored.AllowCharacterPasswordAccountLogin).IsTrue();
		await Assert.That((await _db.GetAccountForCharacterAsync(new DBRef(1)))!.Id).IsEqualTo(ordinary.Id);
		await Assert.That((await _db.GetAccountByIdAsync(social.Id!))!.AllowCharacterPasswordAccountLogin).IsFalse();
	}

	[Test]
	public async Task PasswordIdentityRemovalRaceCannotRemoveBothAndFallbackNeverCounts()
	{
		var account = await _db.CreateAccountAsync("Owner", null, "hash");
		(await _db.AttachExternalIdentityAsync(account.Id!, Key(), null)).Expect<ExternalIdentity>();
		var results = await Task.WhenAll(_db.TryRemoveAccountPasswordAsync(account.Id!, ["google"]).AsTask(),
			_db.RemoveExternalIdentityAsync(account.Id!, Key(), ["google"]).AsTask());
		await Assert.That(results.Count(x => x is Success)).IsEqualTo(1);
		if ((await _db.GetAccountByIdAsync(account.Id!))!.PasswordHash is not null)
			await Assert.That(await _db.TryRemoveAccountPasswordAsync(account.Id!, ["google"]) is Error<string>).IsTrue();
		else
			await Assert.That(await _db.RemoveExternalIdentityAsync(account.Id!, Key(), ["google"]) is Error<string>).IsTrue();
	}

	[Test]
	public async Task PasskeyAndExternalIdentityRemovalShareTransactionalFinalMethodGuard()
	{
		var account = (await _db.ProvisionExternalAccountAsync(Key(), "PasskeyOwner", null)).Expect<SharpAccount>();
		var credential = new byte[] { 1, 2, 3 };
		await _db.AddAccountPasskeyAsync(new AccountPasskey(account.Id!, credential, [4, 5], 0, "Key", [], false, DateTimeOffset.UtcNow, null), 10);
		var results = await Task.WhenAll(_db.TryRemoveAccountPasskeyAsync(account.Id!, credential, ["google"]).AsTask(),
			_db.RemoveExternalIdentityAsync(account.Id!, Key(), ["google"]).AsTask());
		await Assert.That(results.Count(x => x is Success)).IsEqualTo(1);
		await Assert.That((await _db.GetAccountPasskeysAsync(account.Id!)).Count + (await _db.GetExternalIdentitiesAsync(account.Id!)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task CredentialEpochTracksChangesButNotIdentityUseOrNoOpLinks()
	{
		var account = await _db.CreateAccountAsync("EpochOwner", null, "hash");
		await _db.UpdateAccountPasswordAsync(account.Id!, "replacement");
		(await _db.AttachExternalIdentityAsync(account.Id!, Key(), null)).Expect<ExternalIdentity>();
		await _db.LinkCharacterToAccountAsync(account.Id!, new DBRef(1));
		await _db.UpdateAccountCharacterPasswordLoginAsync(account.Id!, false);
		var epoch = (await _db.GetAccountByIdAsync(account.Id!))!.CredentialEpoch;
		await Assert.That(epoch).IsEqualTo(4);
		await _db.LinkCharacterToAccountAsync(account.Id!, new DBRef(1));
		await _db.UpdateAccountCharacterPasswordLoginAsync(account.Id!, false);
		await Assert.That(await _db.RecordExternalIdentityUseAsync(account.Id!, Key(), DateTimeOffset.UtcNow)).IsTrue();
		await Assert.That(await _db.RecordExternalIdentityUseAsync("node_accounts/other", Key(), DateTimeOffset.UtcNow)).IsFalse();
		await Assert.That((await _db.GetAccountByIdAsync(account.Id!))!.CredentialEpoch).IsEqualTo(epoch);
		(await _db.RemoveExternalIdentityAsync(account.Id!, Key(), [])).Expect<Success>();
		await _db.UnlinkCharacterFromAccountAsync(account.Id!, new DBRef(1));
		await Assert.That((await _db.GetAccountByIdAsync(account.Id!))!.CredentialEpoch).IsEqualTo(epoch + 2);
	}

	[Test]
	public async Task SameIdentityConcurrentProvisionCreatesOnlyOneAccount()
	{
		var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => _db.ProvisionExternalAccountAsync(Key(), "User" + i, "display").AsTask()));
		await Assert.That(results.Count(x => x is SharpAccount)).IsEqualTo(1);
		await Assert.That((await _db.GetAllAccountsAsync()).Count).IsEqualTo(1);
		var account = results.Single(x => x is SharpAccount).Expect<SharpAccount>();
		await Assert.That(account.PasswordHash).IsNull();
		await Assert.That(account.AllowCharacterPasswordAccountLogin).IsFalse();
		await Assert.That((await _db.GetExternalIdentityAsync(Key())).Expect<ExternalIdentity>().AccountId).IsEqualTo(account.Id);
	}

	[Test]
	public async Task UsernameConflictLeavesIdentityAvailableForRetryAndNeverMergesEmail()
	{
		var existing = await _db.CreateAccountAsync("Taken", "same@example.com", "hash");
		await Assert.That(await _db.ProvisionExternalAccountAsync(Key(), "Taken", "same@example.com") is Error<string>).IsTrue();
		await Assert.That(await _db.GetExternalIdentityAsync(Key()) is NotFound).IsTrue();
		var created = (await _db.ProvisionExternalAccountAsync(Key(), "Different", "same@example.com")).Expect<SharpAccount>();
		await Assert.That(created.Id).IsNotEqualTo(existing.Id);
		await Assert.That(created.Email).IsNull();
	}

	[Test]
	public async Task MultipleSubjectsFromOneRegistrationCanLinkAndOnlyOneAccountWinsARace()
	{
		var a = await _db.CreateAccountAsync("A", null, "hash");
		var b = await _db.CreateAccountAsync("B", null, "hash");
		var results = await Task.WhenAll(_db.AttachExternalIdentityAsync(a.Id!, Key(), "first").AsTask(), _db.AttachExternalIdentityAsync(b.Id!, Key(), "first").AsTask());
		await Assert.That(results.Count(x => x is ExternalIdentity)).IsEqualTo(1);
		var winner = results.Single(x => x is ExternalIdentity).Expect<ExternalIdentity>();
		(await _db.AttachExternalIdentityAsync(winner.AccountId, Key("second"), "second")).Expect<ExternalIdentity>();
		await Assert.That((await _db.GetExternalIdentitiesAsync(winner.AccountId)).Count).IsEqualTo(2);
	}

	[Test]
	[Arguments(AccountStatus.Disabled)]
	[Arguments(AccountStatus.Deleted)]
	public async Task ExistingIdentityCannotReprovisionAnUnavailableAccount(AccountStatus status)
	{
		var account = (await _db.ProvisionExternalAccountAsync(Key(), "Original", null)).Expect<SharpAccount>();
		await _db.UpdateAccountStatusAsync(account.Id!, status);
		await Assert.That(await _db.ProvisionExternalAccountAsync(Key(), "Replacement", null) is Error<string>).IsTrue();
		await Assert.That((await _db.GetAllAccountsAsync()).Count).IsEqualTo(1);
		await Assert.That(await _db.AttachExternalIdentityAsync(account.Id!, Key("another"), null) is Error<string>).IsTrue();
	}

	[Test]
	public async Task ParallelUnlinkPreservesOneUsableMethodAndDisabledProvidersDoNotCount()
	{
		var account = (await _db.ProvisionExternalAccountAsync(Key(), "Social", null)).Expect<SharpAccount>();
		(await _db.AttachExternalIdentityAsync(account.Id!, Key("second"), null)).Expect<ExternalIdentity>();
		var results = await Task.WhenAll(_db.RemoveExternalIdentityAsync(account.Id!, Key(), ["google"]).AsTask(), _db.RemoveExternalIdentityAsync(account.Id!, Key("second"), ["google"]).AsTask());
		await Assert.That(results.Count(x => x is Success)).IsEqualTo(1);
		await Assert.That((await _db.GetExternalIdentitiesAsync(account.Id!)).Count).IsEqualTo(1);
		var remaining = (await _db.GetExternalIdentitiesAsync(account.Id!)).Single();
		await Assert.That(await _db.RemoveExternalIdentityAsync(account.Id!, remaining.Key, []) is Error<string>).IsTrue();
		await _db.UpdateAccountPasswordAsync(account.Id!, "hash");
		(await _db.RemoveExternalIdentityAsync(account.Id!, remaining.Key, [])).Expect<Success>();
	}

	[Test]
	public async Task CanonicalIdentityTupleSeparatesComponentsAndChecksFullStoredValues()
	{
		var a = await _db.CreateAccountAsync("A", null, "hash");
		var first = new ExternalIdentityKey("a", "bc", "d");
		var second = new ExternalIdentityKey("ab", "c", "d");
		var linked = (await _db.AttachExternalIdentityAsync(a.Id!, first, null)).Expect<ExternalIdentity>();
		(await _db.AttachExternalIdentityAsync(a.Id!, second, null)).Expect<ExternalIdentity>();
		await Assert.That((await _db.GetExternalIdentitiesAsync(a.Id!)).Count).IsEqualTo(2);
		await _db.Store.WriteAsync(tx =>
		{
			var bytes = Convert.FromHexString(linked.Id);
			var record = Codec.Deserialize<ExternalIdentityRecord>(tx.Range(Tables.ExternalIdentity, []).Single(e => e.Key.SequenceEqual(bytes)).Value);
			tx.Put(Tables.ExternalIdentity, bytes, Codec.Serialize(record with { Subject = "corrupt" }));
		});
		await Assert.That(await _db.GetExternalIdentityAsync(first) is NotFound).IsTrue();
		await Assert.That(await _db.AttachExternalIdentityAsync(a.Id!, first, null) is Error<string>).IsTrue();
	}
}
