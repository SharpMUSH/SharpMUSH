namespace SharpMUSH.Library.Models;

/// <summary>
/// One completed hot copy of the world, sitting in the backup root as a self-contained directory a
/// snapshot tool can read while the game keeps running.
/// </summary>
/// <param name="Name">The directory's name, a UTC timestamp that sorts chronologically.</param>
/// <param name="Path">Full path to the copy.</param>
/// <param name="CreatedAt">When the copy was taken, persisted at creation (legacy copies use the directory timestamp).</param>
/// <param name="SizeBytes">Total size of the copy on disk.</param>
public sealed record WorldBackup(string Name, string Path, DateTimeOffset CreatedAt, long SizeBytes)
{
	public BackupProvenance? Provenance { get; init; }
	public string Status { get; init; } = "legacy; completion unrecorded";
	public bool IsLatest { get; init; }
	public bool IsRecoverable { get; init; } = true;
}

/// <summary>Captured by the initiating operation, never inferred from a backup directory name.</summary>
public sealed record BackupProvenance(string Trigger, string? Initiator = null, string? Operation = null, string? Package = null);

/// <summary>Written alongside the payload before its directory is promoted.</summary>
public sealed record WorldBackupMetadata(int Version, DateTimeOffset CreatedAt, BackupProvenance Provenance, bool Complete);

/// <summary>A backup that was opened and read through: how many tables and entries it holds.</summary>
/// <param name="Backup">The copy that was checked.</param>
/// <param name="Tables">The tables read.</param>
/// <param name="Entries">The entries read, across every table.</param>
public sealed record WorldBackupCheck(WorldBackup Backup, int Tables, long Entries);
