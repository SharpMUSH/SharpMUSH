using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Tests.Database;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Tests.Services;

public class ImportPromotionJournalTests
{
	private string _root = null!;

	[Before(Test)]
	public void Setup() => _root = Path.Combine(Path.GetTempPath(), "import-promotion-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_root);

	private static LightningDatabase Open(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static async Task Populate(string path, string sentinel)
	{
		await using var world = Open(path);
		await world.Migrate();
		await world.CreateRoomAsync(sentinel, (await world.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>());
	}

	private static bool Contains(LightningDatabase world, string sentinel)
		=> world.Store.Read(tx => tx.Dups(Tables.ObjName, Keys.Lower(sentinel)).Any());

	[Test]
	public async Task RestartPromotionReplacesAClosedWorldAndRetainsItsMatchingRollbackPair()
	{
		var live = Path.Combine(_root, "live");
		var journal = new ImportPromotionJournal(live);
		var session = journal.Create();
		await Populate(live, "Live sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		Directory.CreateDirectory(journal.LiveKeyRingPath);
		File.WriteAllText(Path.Combine(journal.LiveKeyRingPath, "key.xml"), "outgoing key");
		Directory.CreateDirectory(session.KeyRingPath);
		File.WriteAllText(Path.Combine(session.KeyRingPath, "key.xml"), "import key");
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		journal.RequestPromotion(session.Id);
		await using (var stillLive = Open(live))
		{
			await Assert.That(Contains(stillLive, "Live sentinel")).IsTrue();
			await Assert.That(Contains(stillLive, "Imported sentinel")).IsFalse();
		}
		journal.ApplyPendingBeforeHostConstruction();
		await using (var reopened = Open(live))
		{
			await reopened.Migrate();
			await Assert.That(Contains(reopened, "Imported sentinel")).IsTrue();
			await Assert.That(Contains(reopened, "Live sentinel")).IsFalse();
		}
		await Assert.That(File.ReadAllText(Path.Combine(journal.LiveKeyRingPath, "key.xml"))).IsEqualTo("import key");
		journal.RequestRollback(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var restored = Open(live);
		await Assert.That(Contains(restored, "Live sentinel")).IsTrue();
		await Assert.That(File.ReadAllText(Path.Combine(journal.LiveKeyRingPath, "key.xml"))).IsEqualTo("outgoing key");
	}

	[Test]
	public async Task CanceledPrecommitAndPreexistingRollbackNeverReplaceLiveData()
	{
		var live = Path.Combine(_root, "live");
		var journal = new ImportPromotionJournal(live);
		var session = journal.Create();
		await Populate(live, "Live sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		await Assert.That(() => journal.RequestPromotion(session.Id, new CancellationToken(true))).Throws<OperationCanceledException>();
		Directory.CreateDirectory(live + ".previous");
		await Assert.That(() => journal.RequestPromotion(session.Id)).Throws<IOException>();
		journal.ApplyPendingBeforeHostConstruction();
		await using var unchanged = Open(live);
		await Assert.That(Contains(unchanged, "Live sentinel")).IsTrue();
	}
	[Test]
	public async Task PostSwapKeyFailureBlocksStartupAndExplicitRollbackRestoresTheOriginalWorld()
	{
		var live = Path.Combine(_root, "live");
		var keyParent = Path.Combine(_root, "key-parent");
		var journal = new ImportPromotionJournal(live, Path.Combine(keyParent, "keys"));
		var session = journal.Create();
		await Populate(live, "Live sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		Directory.CreateDirectory(session.KeyRingPath);
		File.WriteAllText(Path.Combine(session.KeyRingPath, "key.xml"), "import key");
		File.WriteAllText(keyParent, "block the key-directory move after the world move");
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		journal.RequestPromotion(session.Id);
		await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<IOException>();
		await Assert.That(journal.Get(session.Id).State).IsEqualTo(ImportSessionState.RecoveryRequired);
		await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<IOException>();
		journal.RequestRollback(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var restored = Open(live);
		await Assert.That(Contains(restored, "Live sentinel")).IsTrue();
	}

	[Test]
	public async Task EngineLeasePreventsASecondProcessFromOpeningTheWorldDuringRestartCommit()
	{
		var journal = new ImportPromotionJournal(Path.Combine(_root, "live"));
		using var owner = journal.AcquireEngineLease();
		await Assert.That(() => journal.AcquireEngineLease()).Throws<IOException>();
	}

	[Test]
	public async Task InterruptedRollbackCanResumeAfterWorldRestoreBeforeKeysRestore()
	{
		var live = Path.Combine(_root, "live");
		var journal = new ImportPromotionJournal(live);
		var session = journal.Create();
		await Populate(live, "Original sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		Directory.CreateDirectory(journal.LiveKeyRingPath);
		await File.WriteAllTextAsync(Path.Combine(journal.LiveKeyRingPath, "key.xml"), "original key");
		Directory.CreateDirectory(session.KeyRingPath);
		await File.WriteAllTextAsync(Path.Combine(session.KeyRingPath, "key.xml"), "imported key");
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		journal.RequestPromotion(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		journal.RequestRollback(session.Id);
		// Interrupt at the real filesystem boundary: world restored, key move still pending.
		Directory.Move(live, Path.Combine(session.Directory, "replaced-world"));
		Directory.Move(live + ".previous", live);
		await File.WriteAllTextAsync(Path.Combine(session.Directory, "replaced-keys"), "block key move after world recovery");
		await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<IOException>();
		await Assert.That(journal.Get(session.Id).State).IsEqualTo(ImportSessionState.RecoveryRequired);
		File.Delete(Path.Combine(session.Directory, "replaced-keys"));
		journal.RequestRollback(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var restored = Open(live);
		await Assert.That(Contains(restored, "Original sentinel")).IsTrue();
		await Assert.That(await File.ReadAllTextAsync(Path.Combine(journal.LiveKeyRingPath, "key.xml"))).IsEqualTo("original key");
	}

	[Test]
	[NotInParallel]
	public async Task OfflineRecoveryWorksWithoutStartingNatsOrOpeningTheImportedProvider()
	{
		var live = Path.Combine(_root, "live");
		var journal = new ImportPromotionJournal(live);
		var session = journal.Create();
		await Populate(live, "Original sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		journal.RequestPromotion(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		var oldPath = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH");
		var oldKeys = Environment.GetEnvironmentVariable("SHARPMUSH_DATAPROTECTION_PATH");
		try
		{
			Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", live);
			Environment.SetEnvironmentVariable("SHARPMUSH_DATAPROTECTION_PATH", journal.LiveKeyRingPath);
			await SharpMUSH.Server.Program.Main("--import-rollback", session.Id);
		}
		finally
		{
			Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", oldPath);
			Environment.SetEnvironmentVariable("SHARPMUSH_DATAPROTECTION_PATH", oldKeys);
		}
		await Assert.That(journal.Get(session.Id).State).IsEqualTo(ImportSessionState.RolledBack);
		await using var restored = Open(live);
		await Assert.That(Contains(restored, "Original sentinel")).IsTrue();
	}

	[Test]
	public async Task MissingStagedKeysRefusePromotionBeforeAnyLiveDirectoryMoves()
	{
		var live = Path.Combine(_root, "live");
		var journal = new ImportPromotionJournal(live);
		var session = journal.Create();
		await Populate(live, "Original sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		journal.MarkReady(session.Id);
		await Assert.That(() => journal.RequestPromotion(session.Id)).Throws<IOException>();
		await using var unchanged = Open(live);
		await Assert.That(Contains(unchanged, "Original sentinel")).IsTrue();
		await Assert.That(Directory.Exists(live + ".previous")).IsFalse();
	}

	[Test]
	public async Task PluginFilesNamedSessionJsonDoNotBecomeImportJournals()
	{
		var journal = new ImportPromotionJournal(Path.Combine(_root, "live"));
		var previous = journal.Create();
		journal.Save(previous with { State = ImportSessionState.Aborted });
		var plugin = Path.Combine(previous.Directory, "plugins", "sample");
		Directory.CreateDirectory(plugin);
		await File.WriteAllTextAsync(Path.Combine(plugin, "session.json"), "plugin-owned non-journal data");
		await Assert.That(journal.Create().State).IsEqualTo(ImportSessionState.Preparing);
	}

	[Test]
	public async Task AuthorizedPromotionIntentRecoversWhenPendingMarkerWasNotPersisted()
	{
		var live = Path.Combine(_root, "live");
		var journal = new ImportPromotionJournal(live);
		var session = journal.Create();
		await Populate(live, "Original sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		journal.RequestPromotion(session.Id);
		File.Delete(Path.Combine(journal.SessionsPath, "pending.json"));
		journal.ApplyPendingBeforeHostConstruction();
		await Assert.That(journal.Get(session.Id).State).IsEqualTo(ImportSessionState.Committed);
		await using var reopened = Open(live);
		await Assert.That(Contains(reopened, "Imported sentinel")).IsTrue();
	}

	[Test]
	public async Task AcceptedPromotionCannotBeOverwrittenByLateCancellation()
	{
		var journal = new ImportPromotionJournal(Path.Combine(_root, "live"));
		var session = journal.Create();
		Directory.CreateDirectory(session.WorldPath);
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		journal.RequestPromotion(session.Id);
		await Assert.That(journal.AbortPreview(session.Id, "late cancellation")).IsFalse();
		await Assert.That(journal.Get(session.Id).State).IsEqualTo(ImportSessionState.PromotionRequested);
		await Assert.That(Directory.Exists(session.WorldPath)).IsTrue();
	}

	[Test]
	public async Task DirectoryWithoutInitialJournalDoesNotBlockStartupOrNextPreview()
	{
		var journal = new ImportPromotionJournal(Path.Combine(_root, "live"));
		var orphan = Path.Combine(journal.SessionsPath, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(orphan);
		File.WriteAllText(Path.Combine(orphan, "session.json.writing-interrupted"), "partial");
		await Assert.That(journal.List().Count).IsEqualTo(0);
		journal.ApplyPendingBeforeHostConstruction();
		await Assert.That(journal.Create().State).IsEqualTo(ImportSessionState.Preparing);
		await Assert.That(Directory.Exists(orphan)).IsTrue();
	}

	[Test]
	public async Task UnreadableExistingJournalFailsClosedInsteadOfLookingUninitialized()
	{
		if (OperatingSystem.IsWindows()) return;
		var journal = new ImportPromotionJournal(Path.Combine(_root, "live"));
		var session = journal.Create();
		var original = File.GetUnixFileMode(session.Directory);
		try
		{
			File.SetUnixFileMode(session.Directory, UnixFileMode.None);
			if (CanStillRead(session.Directory))
			{
				Skip.Test("This environment reads directories without read permission (root); the Unix permission failure cannot be exercised.");
				return;
			}
			await Assert.That(() => journal.List()).Throws<UnauthorizedAccessException>();
			await Assert.That(journal.ApplyPendingBeforeHostConstruction).Throws<UnauthorizedAccessException>();
		}
		finally { File.SetUnixFileMode(session.Directory, original); }
	}

	[Test]
	public async Task PromotionAndRollbackKeepCurrentPortalCredentialsAndOnlyMappedGodOwnership()
	{
		var live = Path.Combine(_root, "live");
		var journal = new ImportPromotionJournal(live);
		var session = journal.Create();
		await Populate(live, "Original sentinel");
		await Populate(session.WorldPath, "Imported sentinel");
		string accountId;
		await using (var original = Open(live))
		{
			var account = await original.CreateAccountAsync("claimed-admin", "admin@example.test", "current-password");
			accountId = account.Id ?? throw new InvalidOperationException("Account id missing.");
			await original.LinkCharacterToAccountAsync(accountId, new DBRef(1));
			await original.SetServerSetupCompletedAsync(true);
		}
		Directory.CreateDirectory(session.KeyRingPath);
		journal.MarkReady(session.Id);
		journal.RequestPromotion(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using (var promoted = Open(live))
		{
			var account = await promoted.GetAccountByUsernameAsync("claimed-admin");
			await Assert.That(account).IsNotNull();
			await Assert.That(account!.PasswordHash).IsEqualTo("current-password");
			await Assert.That(account.Id).IsEqualTo(accountId);
			await Assert.That((await promoted.GetServerStateAsync()).SetupCompleted).IsTrue();
			await promoted.UpdateAccountPasswordAsync(accountId, "changed-after-promotion");
		}
		journal.RequestRollback(session.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var restored = Open(live);
		await Assert.That((await restored.GetAccountByIdAsync(accountId))!.PasswordHash).IsEqualTo("changed-after-promotion");
	}

	private static bool CanStillRead(string directory)
	{
		try { _ = Directory.GetFileSystemEntries(directory); return true; }
		catch (UnauthorizedAccessException) { return false; }
	}

}
