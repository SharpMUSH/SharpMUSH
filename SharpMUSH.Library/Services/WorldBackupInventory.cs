using System.Globalization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>A shared, bounded view of the independent world and pre-package retention groups.</summary>
public sealed record WorldBackupInventory(IReadOnlyList<BackupInventoryEntry> Entries, int Total, int Pages)
{
	public const int PageSize = 10;

	public static WorldBackupInventory Read(IWorldBackupService service, int page)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
		var entries = service.List().Select(backup => new BackupInventoryEntry(backup, "world", service.Keep))
			.Concat(service.ListPackageOperationBackups().Select(backup => new BackupInventoryEntry(backup, "pre-package", service.PackageOperationKeep)))
			.OrderByDescending(entry => entry.Backup.CreatedAt).ThenBy(entry => entry.Backup.Path, StringComparer.Ordinal).ToArray();
		var skip = Math.Min((long)(page - 1) * PageSize, entries.Length);
		return new WorldBackupInventory(entries.Skip((int)skip).Take(PageSize).ToArray(), entries.Length,
			Math.Max(1, (int)(((long)entries.Length + PageSize - 1) / PageSize)));
	}
}

/// <summary>Display values independent of terminal layout, with explicit UTC and exact on-disk bytes.</summary>
public sealed record BackupInventoryEntry(WorldBackup Backup, string Group, int Keep)
{
	public string Timestamp => Backup.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture);
	public string Reason => Backup.Provenance?.Trigger ?? "unknown/legacy";
	public string Size
	{
		get
		{
			var bytes = Backup.SizeBytes;
			var human = bytes switch
			{
				>= 1L << 30 => FormattableString.Invariant($"{bytes / (double)(1L << 30):F1} GiB"),
				>= 1L << 20 => FormattableString.Invariant($"{bytes / (double)(1L << 20):F1} MiB"),
				>= 1L << 10 => FormattableString.Invariant($"{bytes / (double)(1L << 10):F1} KiB"),
				_ => $"{bytes} B"
			};
			return FormattableString.Invariant($"{human} ({bytes} bytes)");
		}
	}
}
