using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class NativeCharacterCredentialsTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private IAccountCredentialRevocation _revoker = null!;
	private AccountCredentialGate _gate = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "native-credentials-" + Guid.NewGuid().ToString("N"));
		_revoker = Substitute.For<IAccountCredentialRevocation>();
		_gate = new();
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null, credentialGate: _gate,
			credentialRevokers: new Lazy<IEnumerable<IAccountCredentialRevocation>>(() => [_revoker]));
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		await FixtureDirectoryCleanup.DeleteAsync(_path);
	}

	private static SharpSession Session(string token) => new()
	{
		Token = token, AccountId = "node_accounts/owner", OriginIp = "203.0.113.9", ExpiryUnixMs = 1000, TtlMs = 500
	};

	[Test]
	public async Task SessionDigestAuthorityTracksExpiryAndEveryDeletionWithoutContainingBearer()
	{
		const string token = "session-bearer-never-in-projection";
		await _db.UpsertSessionAsync(Session(token));
		var digest = AccountSessionDigest.Compute(token);
		await Assert.That((await _db.GetSessionAuthorityByDigestAsync(digest)).Expect<AccountSessionAuthority>().ExpiryUnixMs).IsEqualTo(1000);
		await _db.TouchSessionExpiryAsync(token, 5000);
		await Assert.That((await _db.GetSessionAuthorityByDigestAsync(digest)).Expect<AccountSessionAuthority>().ExpiryUnixMs).IsEqualTo(5000);
		var projection = _db.Store.Read(tx => tx.Range(Tables.SessionDigest, []).Single().Value);
		await Assert.That(System.Text.Encoding.UTF8.GetString(projection).Contains(token, StringComparison.Ordinal)).IsFalse();
		await _db.DeleteSessionsForIpAsync("203.0.113.9");
		await Assert.That(await _db.GetSessionAuthorityByDigestAsync(digest) is NotFound).IsTrue();
		await _db.UpsertSessionAsync(Session(token));
		await _db.DeleteExpiredSessionsAsync(1000, 10);
		await Assert.That(await _db.GetSessionAuthorityByDigestAsync(digest) is NotFound).IsTrue();
		await _db.UpsertSessionAsync(Session(token));
		await _db.DeleteSessionsForAccountAsync("node_accounts/owner");
		await Assert.That(await _db.GetSessionAuthorityByDigestAsync(digest) is NotFound).IsTrue();
	}

	[Test]
	public async Task SessionDigestMigrationBackfillsHistoricalTokensWithoutChangingThem()
	{
		const string token = "historical-source-session";
		await _db.UpsertSessionAsync(Session(token));
		await _db.Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.SessionDigestMigrationId));
			tx.Delete(Tables.SessionDigest, Convert.FromHexString(AccountSessionDigest.Compute(token)));
		});
		await _db.Migrate();
		await _db.Migrate();
		await Assert.That((await _db.GetSessionAsync(token))!.Token).IsEqualTo(token);
		await Assert.That((await _db.GetSessionAuthorityByDigestAsync(AccountSessionDigest.Compute(token))).Expect<AccountSessionAuthority>().AccountId).IsEqualTo("node_accounts/owner");
	}

	[Test]
	public async Task EmailChangesAdvanceCredentialAuthorityButIdenticalWritesDoNot()
	{
		var account = await _db.CreateAccountAsync("EmailOwner", "first@example.com", "hash");
		await _db.LinkCharacterToAccountAsync(account.Id!, new DBRef(1));
		var player = (await _db.GetCharactersForAccountAsync(account.Id!)).Single();
		var before = (await _db.GetNativeCharacterAuthorityAsync(account.Id!, player.Object.DBRef)).Expect<NativeCharacterAuthority>();
		await _db.UpdateAccountEmailAsync(account.Id!, "first@example.com");
		await Assert.That((await _db.GetNativeCharacterAuthorityAsync(account.Id!, player.Object.DBRef)).Expect<NativeCharacterAuthority>().AccountCredentialEpoch).IsEqualTo(before.AccountCredentialEpoch);
		await _db.UpdateAccountEmailAsync(account.Id!, "second@example.com");
		await Assert.That((await _db.GetNativeCharacterAuthorityAsync(account.Id!, player.Object.DBRef)).Expect<NativeCharacterAuthority>().AccountCredentialEpoch).IsEqualTo(before.AccountCredentialEpoch + 1);
	}

	[Test]
	public async Task OwnedPasswordWriteChecksFullIdentityAndOwnerAtTransactionBoundary()
	{
		var account = await _db.CreateAccountAsync("Owner", null, "account-hash");
		await _db.LinkCharacterToAccountAsync(account.Id!, new DBRef(1));
		var player = (await _db.GetCharactersForAccountAsync(account.Id!)).Single();
		var initial = (await _db.GetNativeCharacterAuthorityAsync(account.Id!, player.Object.DBRef)).Expect<NativeCharacterAuthority>();
		await Assert.That(await _db.SetOwnedPlayerPasswordAsync("node_accounts/other", player.Object.DBRef, "bad") is Error<string>).IsTrue();
		await Assert.That(await _db.SetOwnedPlayerPasswordAsync(account.Id!, new DBRef(1, player.Object.CreationTime - 1), "bad") is Error<string>).IsTrue();
		(await _db.SetOwnedPlayerPasswordAsync(account.Id!, player.Object.DBRef, "character-hash")).Expect<Success>();
		var updated = (await _db.GetNativeCharacterAuthorityAsync(account.Id!, player.Object.DBRef)).Expect<NativeCharacterAuthority>();
		await Assert.That(updated.PasswordCredentialEpoch).IsEqualTo(initial.PasswordCredentialEpoch + 1);
		await Assert.That((await _db.GetAccountByIdAsync(account.Id!))!.PasswordHash).IsEqualTo("account-hash");
		await _db.SetPlayerPasswordAsync(player, "game-command-hash");
		await Assert.That((await _db.GetNativeCharacterAuthorityAsync(account.Id!, player.Object.DBRef)).Expect<NativeCharacterAuthority>().PasswordCredentialEpoch).IsEqualTo(initial.PasswordCredentialEpoch + 2);
		await _db.UnlinkCharacterFromAccountAsync(account.Id!, player.Object.DBRef);
		await Assert.That(await _db.GetNativeCharacterAuthorityAsync(account.Id!, player.Object.DBRef) is NotFound).IsTrue();
		await Assert.That(await _db.SetOwnedPlayerPasswordAsync(account.Id!, player.Object.DBRef, "bad") is Error<string>).IsTrue();
	}
	[Test]
	public async Task CommittedAuthorityChangesPurgeOnlyTheAffectedAccountAndNoOpEmailKeepsProofs()
	{
		var owner = await _db.CreateAccountAsync("purge-owner", "before@example.test", "hash");
		var other = await _db.CreateAccountAsync("purge-other", null, "hash");
		await _db.UpdateAccountEmailAsync(owner.Id!, "after@example.test");
		_revoker.Received(1).RevokeAccount(owner.Id!);
		_revoker.DidNotReceive().RevokeAccount(other.Id!);
		await _db.UpdateAccountEmailAsync(owner.Id!, "after@example.test");
		_revoker.Received(1).RevokeAccount(owner.Id!);
		await _db.UpdateAccountMustChangePasswordAsync(owner.Id!, true);
		_revoker.Received(2).RevokeAccount(owner.Id!);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task WorldReplacementWaitsForTheCurrentCredentialDecision(bool promote)
	{
		var original = await _db.CreateAccountAsync("before-world-replacement", null, "hash");
		var staging = promote ? await _db.CreateStagingAsync() : null;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var holding = _gate.RunAsync(async () => { entered.SetResult(); await release.Task; });
		await entered.Task;
		Task? replacement = null;
		try
		{
			replacement = staging is not null ? staging.PromoteToLiveAsync() : _db.WipeDatabaseAsync().AsTask();
			await Assert.That(await _db.GetAccountByIdAsync(original.Id!) is not null).IsTrue();
			await Assert.That(replacement.IsCompleted).IsFalse();
		}
		finally
		{
			release.SetResult();
			await holding;
			if (replacement is not null) await replacement;
			if (staging is not null) await staging.DisposeAsync();
			if (Directory.Exists(_path + ".previous")) await FixtureDirectoryCleanup.DeleteAsync(_path + ".previous");
		}
	}

}
