using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class WorldBackupInventoryTests
{
	private sealed class Backups(string root) : IWorldBackupService
	{
		public WorldBackupWriter World { get; } = new(new WorldBackupOptions { Root = root, Keep = 30 }, Write, NullLogger.Instance);
		public WorldBackupWriter Packages { get; } = new(new WorldBackupOptions { Root = Path.Join(root, "pre-package"), Keep = 30 }, Write, NullLogger.Instance);
		private static ValueTask Write(string path, CancellationToken ct)
		{
			File.WriteAllBytes(Path.Join(path, "world"), new byte[2048]);
			return ValueTask.CompletedTask;
		}
		public bool IsSupported => true;
		public string UnavailableReason => "";
		public string Root => root;
		public int Keep => 30;
		public int PackageOperationKeep => 30;
		public TimeSpan ScheduledInterval => TimeSpan.Zero;
		public ValueTask<Result<WorldBackup>> CreateAsync(CancellationToken ct = default) => World.CreateAsync(ct);
		public ValueTask<Result<WorldBackup>> CreateBeforePackageOperationAsync(CancellationToken ct = default) => Packages.CreateAsync(ct);
		public IReadOnlyList<WorldBackup> List() => World.List();
		public IReadOnlyList<WorldBackup> ListPackageOperationBackups() => Packages.List();
		public ValueTask<Result<WorldBackupCheck>> CheckLatestAsync(CancellationToken ct = default) => throw new NotSupportedException();
	}

	[Test]
	public async Task PagesCoverBothGroupsOnceAndKeepContextWithEachCopy()
	{
		var root = Path.Join(Path.GetTempPath(), "backup-inventory-" + Guid.NewGuid().ToString("N"));
		try
		{
			var service = new Backups(root);
			for (var i = 0; i < 13; i++) await service.World.CreateAsync(new BackupProvenance("scheduled"));
			for (var i = 0; i < 12; i++) await service.Packages.CreateAsync(new BackupProvenance("pre-package", "admin", "update", "example"));
			var pages = Enumerable.Range(1, 3).Select(page => WorldBackupInventory.Read(service, page)).ToArray();
			await Assert.That(pages.Select(page => page.Entries.Count)).IsEquivalentTo([10, 10, 5]);
			await Assert.That(pages.All(page => page.Total == 25 && page.Pages == 3)).IsTrue();
			var entries = pages.SelectMany(page => page.Entries).ToArray();
			await Assert.That(entries.Select(entry => entry.Backup.Path).Distinct().Count()).IsEqualTo(25);
			await Assert.That(entries.Count(entry => entry.Group == "world")).IsEqualTo(13);
			await Assert.That(entries.Count(entry => entry.Group == "pre-package" && entry.Backup.Provenance?.Package == "example")).IsEqualTo(12);
			await Assert.That(entries.Count(entry => entry.Backup.IsLatest)).IsEqualTo(2);
			await Assert.That(WorldBackupInventory.Read(service, int.MaxValue).Entries.Count).IsEqualTo(0);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	[Test]
	public async Task DisplayUsesUtcExactBytesAndHonestLegacyReason()
	{
		var backup = new WorldBackup("old", "/world/old", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)), 1536);
		var entry = new BackupInventoryEntry(backup, "world", 2);
		var oldCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
			await Assert.That(entry.Timestamp).IsEqualTo("2026-01-02 00:04:05.000 UTC");
			await Assert.That(entry.Size).IsEqualTo("1.5 KiB (1536 bytes)");
			await Assert.That(entry.Reason).IsEqualTo("unknown/legacy");
		}
		finally { CultureInfo.CurrentCulture = oldCulture; }
	}
}
