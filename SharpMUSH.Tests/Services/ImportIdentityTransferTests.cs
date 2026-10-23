using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;
using SharpMUSH.Tests.Database;

namespace SharpMUSH.Tests.Services;

public class ImportIdentityTransferTests
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "import-identity-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_root);

	private static LightningDatabase Open(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private async Task<(ImportPromotionJournal Journal, ImportSession Session)> Prepare()
	{
		var journal = new ImportPromotionJournal(Path.Combine(_root, "live"));
		var session = journal.Create();
		await using (var source = Open(journal.LiveWorldPath)) await source.Migrate();
		await using (var target = Open(session.WorldPath)) await target.Migrate();
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		return (journal, session);
	}

	[Test]
	public async Task FactorsBansAndPoliciesSurviveButSessionsAndUnmappedOwnershipDoNot()
	{
		var (journal, session) = await Prepare();
		string accountId;
		string externalAccountId;
		var external = new ExternalIdentityKey("test", "https://issuer.example", "subject");
		await using (var source = Open(journal.LiveWorldPath))
		{
			var account = await source.CreateAccountAsync("passkey-only", "owner@example.test", "");
			accountId = account.Id!;
			await source.AddAccountPasskeyAsync(new(accountId, [1, 2, 3], [4, 5, 6], 42, "Key", ["usb"], true, DateTimeOffset.UtcNow, null), 10);
			externalAccountId = (await source.ProvisionExternalAccountAsync(external, "external-only", null)).Expect<SharpAccount>().Id!;
			await source.UpdateAccountCharacterPasswordLoginAsync(accountId, false);
			await source.UpdateAccountMustChangePasswordAsync(accountId, true);
			await source.LinkCharacterToAccountAsync(accountId, new DBRef(1));
			await source.Store.WriteAsync(tx =>
			{
				var key = Keys.Str(accountId.Split('/')[1]);
				tx.Put(Tables.AccountChar.Forward, key, Keys.Dbref(2));
				tx.Put(Tables.AccountChar.Reverse, Keys.Dbref(2), key);
			});
			await source.BanAccountAsync(new(accountId, "Preserved ban", null, DateTimeOffset.UtcNow, null));
			await source.Store.WriteAsync(tx => tx.Put(Tables.Session, Keys.Str("old-world-session"), [1]));
		}
		// Staged and retained worlds may contain authority from their previous lifetime. Replacement must remove it.
		await using (var target = Open(session.WorldPath))
			await target.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.Session, Keys.Str("stale-session"), [1]);
				tx.Put(Tables.OAuthGrant, Keys.Str("stale-grant"), [1]);
				tx.Put(Tables.OAuthToken, Keys.Str("stale-token"), [1]);
			});
		journal.RequestPromotion(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using (var target = Open(journal.LiveWorldPath))
		{
			var account = (await target.GetAccountByIdAsync(accountId))!;
			await Assert.That(account.Status).IsEqualTo(AccountStatus.Disabled);
			await Assert.That(account.AllowCharacterPasswordAccountLogin).IsFalse();
			await Assert.That(account.MustChangePassword).IsTrue();
			await Assert.That((await target.GetAccountBanAsync(accountId))?.Reason).IsEqualTo("Preserved ban");
			await Assert.That((await target.GetAccountPasskeyAsync([1, 2, 3]))?.SignCount).IsEqualTo(42u);
			await Assert.That((await target.GetExternalIdentitiesAsync(externalAccountId)).Count).IsEqualTo(1);
			await Assert.That((await target.GetAccountByIdAsync(externalAccountId))?.AllowCharacterPasswordAccountLogin ?? true).IsFalse();
			await Assert.That((await target.GetAccountByIdAsync(externalAccountId))?.PasswordHash).IsNull();
			await Assert.That((await target.GetAccountForCharacterAsync(new DBRef(1)))?.Id).IsEqualTo(accountId);
			await Assert.That(await target.GetAccountForCharacterAsync(new DBRef(2))).IsNull();
			await Assert.That(target.Store.Read(tx => tx.Count(Tables.Session) + tx.Count(Tables.OAuthGrant) + tx.Count(Tables.OAuthToken))).IsEqualTo(0);
			await Assert.That((await target.CreateAccountAsync("new", null, "new-password")).Id).IsNotEqualTo(accountId);
			await target.RemoveAccountPasskeyAsync(accountId, [1, 2, 3]);
			await target.UnlinkCharacterFromAccountAsync(accountId, new DBRef(1));
		}
		journal.RequestRollback(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var restored = Open(journal.LiveWorldPath);
		await Assert.That(await restored.GetAccountPasskeyAsync([1, 2, 3])).IsNull();
		await Assert.That(await restored.GetAccountForCharacterAsync(new DBRef(1))).IsNull();
		await Assert.That(restored.Store.Read(tx => tx.Count(Tables.Session))).IsEqualTo(0);
		await Assert.That(await restored.GetAccountForCharacterAsync(new DBRef(2))).IsNull();
	}

	[Test]
	[Arguments("duplicate-user")]
	[Arguments("missing-role")]
	public async Task InvalidIdentityFailsBeforeWorldSwapAndDoesNotPartiallyWriteTarget(string damage)
	{
		var (journal, session) = await Prepare();
		await using (var source = Open(journal.LiveWorldPath))
		{
			var account = await source.CreateAccountAsync("owner", null, "source-password");
			await source.Store.WriteAsync(tx =>
			{
				if (damage == "duplicate-user")
					tx.Put(Tables.Account, Keys.Str("999"), Codec.Serialize(new AccountRecord { Username = "OWNER" }));
				else tx.Put(Tables.AccountRole, Keys.Str(account.Id!.Split('/')[1]), Keys.Str("missing-role"));
			});
		}
		await using (var staged = Open(session.WorldPath)) await staged.CreateAccountAsync("staged", null, "staged-password");
		journal.RequestPromotion(session.Id);
		await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<InvalidDataException>();
		await Assert.That(Directory.Exists(journal.LiveWorldPath + ".previous")).IsFalse();
		await using var unchanged = Open(session.WorldPath);
		await Assert.That((await unchanged.GetAccountByUsernameAsync("staged"))?.PasswordHash).IsEqualTo("staged-password");
		await Assert.That(await unchanged.GetAccountByUsernameAsync("owner")).IsNull();
	}
	[Test]
	public async Task FailedPreSwapTransferRollsBackWithoutDroppingOriginalCharacterLinks()
	{
		var (journal, session) = await Prepare();
		string accountId;
		await using (var source = Open(journal.LiveWorldPath))
		{
			accountId = (await source.CreateAccountAsync("owner", null, "password")).Id!;
			await source.LinkCharacterToAccountAsync(accountId, new DBRef(1));
			await source.Store.WriteAsync(tx => tx.Put(Tables.AccountRole, Keys.Str(accountId.Split('/')[1]), Keys.Str("missing-role")));
		}
		journal.RequestPromotion(session.Id);
		await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<InvalidDataException>();
		journal.RequestRollback(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var restored = Open(journal.LiveWorldPath);
		await Assert.That((await restored.GetAccountForCharacterAsync(new DBRef(1)))?.Id).IsEqualTo(accountId);
		await Assert.That((await restored.GetAccountByIdAsync(accountId))?.PasswordHash).IsEqualTo("password");
	}

	[Test]
	public async Task RollbackRejectsRecycledMappedPlayerBeforeChangingEitherWorld()
	{
		var (journal, session) = await Prepare();
		await using (var source = Open(journal.LiveWorldPath))
		{
			var account = await source.CreateAccountAsync("owner", null, "password");
			await source.LinkCharacterToAccountAsync(account.Id!, new DBRef(1));
		}
		journal.RequestPromotion(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using (var promoted = Open(journal.LiveWorldPath))
			await promoted.Store.WriteAsync(tx =>
			{
				tx.TryGet(Tables.Obj, Keys.Dbref(1), out var bytes);
				var player = Codec.Deserialize<ObjectRecord>(bytes);
				tx.Put(Tables.Obj, Keys.Dbref(1), Codec.Serialize(player with { CreationTime = player.CreationTime + 1 }));
			});
		journal.RequestRollback(session.Id);
		await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<InvalidDataException>();
		await Assert.That(Directory.Exists(journal.LiveWorldPath + ".previous")).IsTrue();
		await Assert.That(Directory.Exists(Path.Combine(session.Directory, "replaced-world"))).IsFalse();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AccountRoleTransferPreservesCustomDefinitionsAndRejectsChangedImportedAuthority(bool conflictingRole)
	{
		var (journal, session) = await Prepare();
		await using (var source = Open(journal.LiveWorldPath))
		{
			var account = await source.CreateAccountAsync("owner", null, "password");
			await source.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.CustomPermission, Keys.Str("custom.authority"), Codec.Serialize(new CustomPermissionRecord { Scope = "custom.authority" }));
				tx.Put(Tables.Role, Keys.Str("custom"), Codec.Serialize(new RoleRecord { Slug = "custom", Permissions = new() { ["custom.authority"] = 1 } }));
				tx.Put(Tables.AccountRole, Keys.Str(account.Id!.Split('/')[1]), Keys.Str("custom"));
			});
		}
		if (conflictingRole)
			await using (var target = Open(session.WorldPath))
				await target.Store.WriteAsync(tx =>
				{
					tx.Put(Tables.Role, Keys.Str("custom"), Codec.Serialize(new RoleRecord { Slug = "custom" }));
					tx.Put(Tables.ObjRole.Forward, Keys.Dbref(1), Keys.Str("custom"));
					tx.Put(Tables.ObjRole.Reverse, Keys.Str("custom"), Keys.Dbref(1));
				});
		journal.RequestPromotion(session.Id);
		if (conflictingRole)
		{
			await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<InvalidDataException>();
			await Assert.That(Directory.Exists(journal.LiveWorldPath + ".previous")).IsFalse();
		}
		else
		{
			journal.ApplyPendingBeforeHostConstruction();
			await using var promoted = Open(journal.LiveWorldPath);
			await Assert.That((await promoted.GetCustomPermissionsAsync()).Any(p => p.Scope == "custom.authority")).IsTrue();
		}
	}

	[Test]
	public async Task RollbackAfterDurableIdentityTransferAndWorldMoveKeepsLatestCredentials()
	{
		var (journal, session) = await Prepare();
		string accountId;
		await using (var original = Open(journal.LiveWorldPath))
		{
			accountId = (await original.CreateAccountAsync("owner", null, "original-password")).Id!;
			await original.LinkCharacterToAccountAsync(accountId, new DBRef(1));
		}
		journal.RequestPromotion(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using (var promoted = Open(journal.LiveWorldPath))
			await promoted.UpdateAccountPasswordAsync(accountId, "latest-password");
		journal.RequestRollback(session.Id);
		session = journal.Get(session.Id);
		LightningDatabase.TransferImportIdentity(journal.LiveWorldPath, journal.LiveWorldPath + ".previous", session.IdentityMapping, rollback: true);
		journal.Save(session with { RollbackIdentityTransferred = true });
		Directory.Move(journal.LiveWorldPath, Path.Combine(session.Directory, "replaced-world"));
		Directory.Move(journal.LiveWorldPath + ".previous", journal.LiveWorldPath);
		journal.ApplyPendingBeforeHostConstruction();
		await using var restored = Open(journal.LiveWorldPath);
		await Assert.That((await restored.GetAccountByIdAsync(accountId))?.PasswordHash).IsEqualTo("latest-password");
		await Assert.That((await restored.GetAccountForCharacterAsync(new DBRef(1)))?.Id).IsEqualTo(accountId);
		await Assert.That(journal.Get(session.Id).State).IsEqualTo(ImportSessionState.RolledBack);
	}

	[Test]
	public async Task SwappedLegacyJournalRefusesRollbackWithoutTouchingEitherIdentityStore()
	{
		var (journal, session) = await Prepare();
		await using (var original = Open(journal.LiveWorldPath)) await original.CreateAccountAsync("original", null, "original-password");
		journal.RequestPromotion(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using (var promoted = Open(journal.LiveWorldPath)) await promoted.CreateAccountAsync("current", null, "current-password");
		journal.Save(journal.Get(session.Id) with { IdentityMappingPrepared = false, IdentityMapping = null });
		journal.RequestRollback(session.Id);
		await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<InvalidOperationException>();
		await Assert.That(Directory.Exists(journal.LiveWorldPath + ".previous")).IsTrue();
		await using var current = Open(journal.LiveWorldPath);
		await using var previous = Open(journal.LiveWorldPath + ".previous");
		await Assert.That((await current.GetAccountByUsernameAsync("current"))?.PasswordHash).IsEqualTo("current-password");
		await Assert.That(await previous.GetAccountByUsernameAsync("current")).IsNull();
		await Assert.That((await previous.GetAccountByUsernameAsync("original"))?.PasswordHash).IsEqualTo("original-password");
	}

}
