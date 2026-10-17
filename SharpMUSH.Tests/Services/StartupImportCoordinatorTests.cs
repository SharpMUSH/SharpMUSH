using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;
using SharpMUSH.Tests.Database;

namespace SharpMUSH.Tests.Services;

public class StartupImportCoordinatorTests
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "startup-import-" + Guid.NewGuid().ToString("N"));
	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_root);

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task StartupUsesAPrivatePreviewAndRequestsOneRestartRatherThanImportingEveryBoot(bool interruptedAfterCommit)
	{
		var live = Path.Combine(_root, "live");
		await using (var world = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = live, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null))
			await world.Migrate();
		var journal = new ImportPromotionJournal(live);
		var previews = new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance);
		var startup = new StartupImportCoordinator(previews, journal, NullLogger<StartupImportCoordinator>.Instance);
		var source = new DatabaseImportFiles(Path.Combine(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.flat"), Format: DatabaseImportFormat.RhostMUSH);
		var first = await startup.PrepareAsync(source);
		await Assert.That(first.State).IsEqualTo(StartupImportState.RestartRequested);
		await Assert.That(journal.Get(first.SessionId!).State).IsEqualTo(ImportSessionState.PromotionRequested);
		journal.ApplyPendingBeforeHostConstruction();
		if (interruptedAfterCommit)
		{
			// Persisted crash boundary: committed session saved, startup receipt/intent cleanup not finished.
			File.Delete(Path.Combine(journal.SessionsPath, "startup-applied.json"));
			await File.WriteAllTextAsync(Path.Combine(journal.SessionsPath, "pending.json"), System.Text.Json.JsonSerializer.Serialize(first.SessionId));
			journal.ApplyPendingBeforeHostConstruction();
		}
		var second = await startup.PrepareAsync(source);
		await Assert.That(second.State).IsEqualTo(StartupImportState.AlreadyApplied);
	}
}
