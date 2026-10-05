namespace SharpMUSH.Library.Models;

/// <summary>
/// Where the world's disk goes, in the four numbers an operator has to tell apart: the map limit (a
/// ceiling the file may grow to), the file's logical length (how far it has grown), what the filesystem
/// has actually allocated for it, and the live data inside it. Plus what a backup run needs and what
/// earlier imports left behind.
/// </summary>
/// <remarks>
/// Deleting records frees pages inside the file for reuse; it never shrinks the file. The file only
/// gets smaller by replacing it with a compacted copy (see <c>deploy/README.md</c>).
/// </remarks>
public sealed record StorageCapacityReport
{
	/// <summary>The world's directory.</summary>
	public required string WorldPath { get; init; }

	/// <summary>The map size: the most the file may ever grow to. A ceiling, not an allocation — it costs
	/// neither disk nor RAM until pages are written.</summary>
	public required long MapSizeBytes { get; init; }

	/// <summary>The environment's page size.</summary>
	public required long PageSize { get; init; }

	/// <summary>The data file's logical length — what <c>ls -l</c> reports.</summary>
	public required long FileBytes { get; init; }

	/// <summary>What the filesystem has allocated for the data file — what <c>du</c> reports. Less than
	/// <see cref="FileBytes"/> where the file is sparse; -1 when this platform cannot say.</summary>
	public required long AllocatedBytes { get; init; }

	/// <summary>Pages the file has reached: the highest page ever written, plus one.</summary>
	public required long FilePages { get; init; }

	/// <summary>Pages holding live data: every table's branch, leaf and overflow pages, plus the two
	/// meta pages.</summary>
	public required long UsedPages { get; init; }

	/// <summary>Disk free on the filesystem holding the world; -1 when it cannot be read.</summary>
	public required long WorldDiskFreeBytes { get; init; }

	/// <summary>What a backup run needs.</summary>
	public required BackupCapacity Backup { get; init; }

	/// <summary>Earlier worlds and half-finished copies still on disk beside the live one.</summary>
	public required IReadOnlyList<LeftoverWorld> Leftovers { get; init; }

	/// <summary>Reader slots freed since startup because the process holding them died mid-read. Each one
	/// pinned pages writers could not reuse until it was freed.</summary>
	public long StaleReadersCleared { get; init; }

	/// <summary>Pages inside the file that hold nothing live: freed by deletes and updates, reused by
	/// later writes, never returned to the filesystem.</summary>
	public long FreePages => Math.Max(0, FilePages - UsedPages);

	/// <summary>The live data, in bytes.</summary>
	public long LiveBytes => UsedPages * PageSize;

	/// <summary>The reusable space inside the file, in bytes.</summary>
	public long FreeBytes => FreePages * PageSize;

	/// <summary>How far the file can still grow before writes fail with a full map.</summary>
	public long MapHeadroomBytes => MapSizeBytes - FilePages * PageSize;

	/// <summary>
	/// How many times its flatfile size a PennMUSH database is assumed to take once imported: every object
	/// and attribute becomes a JSON record with its own index rows, in B-tree pages that are rarely full.
	/// Deliberately generous — the check it feeds refuses only an import that clearly cannot fit.
	/// </summary>
	public const int ImportExpansionFactor = 3;

	/// <summary>
	/// Why an import of <paramref name="sourceBytes"/> of PennMUSH files will not fit, or empty when it will.
	/// The import writes into the live world, so it needs the room inside the map (free pages first, then
	/// headroom) and the room on the disk for whatever the file grows by.
	/// </summary>
	public string ImportShortfall(long sourceBytes)
	{
		var needed = sourceBytes * ImportExpansionFactor;
		var room = FreeBytes + MapHeadroomBytes;
		if (room < needed)
		{
			return $"the world's map has room for about {room} more bytes and an import of {sourceBytes} bytes of "
				+ $"PennMUSH files needs about {needed}. Raise SHARPMUSH_LIGHTNING_MAPSIZE and restart first.";
		}

		var growth = Math.Max(0, needed - FreeBytes);
		var required = growth + Math.Max(growth / 10, 16L << 20);
		return growth > 0 && WorldDiskFreeBytes >= 0 && WorldDiskFreeBytes < required
			? $"the world's disk has {WorldDiskFreeBytes} bytes free and an import of {sourceBytes} bytes of PennMUSH "
				+ $"files needs about {required}. Free space on it first."
			: string.Empty;
	}
}

/// <summary>
/// What the backup directory holds and what the next run will need. A run writes its copy beside the
/// existing ones and prunes only once the new one is complete, so at its peak the directory holds every
/// kept copy plus the new one.
/// </summary>
/// <param name="Root">The backup directory.</param>
/// <param name="Keep">How many copies a run leaves behind.</param>
/// <param name="Copies">How many completed copies are on disk now.</param>
/// <param name="CopiesBytes">Their total size.</param>
/// <param name="NextCopyBytes">The estimated size of the next copy: the live data for a compacting
/// copy, the whole used file for one that is not.</param>
/// <param name="RequiredFreeBytes">The free space a run checks for before it starts: the next copy and a
/// margin.</param>
/// <param name="DiskFreeBytes">Disk free on the backup directory's filesystem; -1 when it cannot be read.</param>
/// <param name="SharesDiskWithWorld">Whether the backups and the world are on one filesystem, so each
/// eats into the other's headroom.</param>
public sealed record BackupCapacity(string Root, int Keep, int Copies, long CopiesBytes, long NextCopyBytes,
	long RequiredFreeBytes, long DiskFreeBytes, bool SharesDiskWithWorld)
{
	/// <summary>The directory's size at the height of a run, before pruning: the kept copies plus the new one.</summary>
	public long PeakBytes => CopiesBytes + NextCopyBytes;

	/// <summary>Whether the next run would pass its free-space check. True when free space cannot be read:
	/// the run itself still reports a disk that fills.</summary>
	public bool NextRunFits => DiskFreeBytes < 0 || DiskFreeBytes >= RequiredFreeBytes;
}

/// <summary>An earlier world, or a copy that never finished, still on disk.</summary>
/// <param name="Path">Where it is.</param>
/// <param name="Kind"><c>previous</c> (the world a promotion replaced), <c>staging</c> (an import never
/// promoted or aborted) or <c>incoming</c> (a backup copy interrupted mid-write).</param>
/// <param name="Bytes">Its size.</param>
public sealed record LeftoverWorld(string Path, string Kind, long Bytes);
