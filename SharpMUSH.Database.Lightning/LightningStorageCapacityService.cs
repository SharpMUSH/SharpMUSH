using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IStorageCapacityService"/> for the Lightning provider: LMDB's own page statistics for what
/// is inside the file, the filesystem for what the file and its neighbours occupy, and the backup
/// service for what a run will need.
/// </summary>
public sealed class LightningStorageCapacityService(LightningDatabase database, IWorldBackupService backups, bool compact)
	: IStorageCapacityService
{
	private const string DataFileName = "data.mdb";

	public StorageCapacityReport Measure()
	{
		var usage = database.Store.Usage();
		var worldPath = database.Store.Path;
		var dataFile = Path.Join(worldPath, DataFileName);
		var fileBytes = File.Exists(dataFile) ? new FileInfo(dataFile).Length : 0;

		return new StorageCapacityReport
		{
			WorldPath = worldPath,
			MapSizeBytes = usage.MapSize,
			PageSize = usage.PageSize,
			FileBytes = fileBytes,
			AllocatedBytes = File.Exists(dataFile) ? FileStat.AllocatedBytes(dataFile) : 0,
			FilePages = usage.FilePages,
			UsedPages = usage.UsedPages,
			WorldDiskFreeBytes = DiskSpace.FreeBytes(worldPath),
			Backup = DescribeBackups(worldPath, usage),
			Leftovers = Leftovers(worldPath)
		};
	}

	private BackupCapacity DescribeBackups(string worldPath, Store.LightningEnvironmentUsage usage)
	{
		var copies = backups.List();
		var next = usage.CopyBytes(compact);
		var root = backups.Root;
		return new BackupCapacity(
			Root: root,
			Keep: backups.Keep,
			Copies: copies.Count,
			CopiesBytes: copies.Sum(c => c.SizeBytes),
			NextCopyBytes: next,
			RequiredFreeBytes: WorldBackupWriter.RequiredFreeBytes(next),
			DiskFreeBytes: string.IsNullOrEmpty(root) ? -1 : DiskSpace.FreeBytes(root),
			SharesDiskWithWorld: Directory.Exists(root) && FileStat.SameDevice(root, worldPath));
	}

	/// <summary>
	/// Everything beside the live world that a promotion, an import or an interrupted backup left: the
	/// world a promotion replaced (<c>&lt;world&gt;.previous</c>, kept until an operator has verified the
	/// new one and deletes it), staging worlds never promoted, and backup copies cut off mid-write.
	/// </summary>
	private IReadOnlyList<LeftoverWorld> Leftovers(string worldPath)
	{
		var found = new List<LeftoverWorld>();
		var trimmed = worldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var previous = trimmed + ".previous";
		if (Directory.Exists(previous)) found.Add(new LeftoverWorld(previous, "previous", DiskSpace.DirectoryBytes(previous)));

		var parent = Path.GetDirectoryName(Path.GetFullPath(trimmed));
		var name = Path.GetFileName(trimmed);
		if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
		{
			found.AddRange(Directory.EnumerateDirectories(parent, name + ".staging-*")
				.Select(dir => new LeftoverWorld(dir, "staging", DiskSpace.DirectoryBytes(dir))));
		}

		if (!string.IsNullOrEmpty(backups.Root) && Directory.Exists(backups.Root))
		{
			found.AddRange(Directory.EnumerateDirectories(backups.Root, WorldBackupWriter.IncomingPrefix + "*")
				.Select(dir => new LeftoverWorld(dir, "incoming", DiskSpace.DirectoryBytes(dir))));
		}

		return found;
	}
}
