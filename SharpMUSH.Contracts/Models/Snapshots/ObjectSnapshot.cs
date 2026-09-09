namespace SharpMUSH.Library.Models.Snapshots;

public sealed record SnapshotAccess(string Name, string[] Flags, string Owner);
public sealed record SnapshotAttribute(string Name, string Markup, string[] Flags, string Owner)
{
	public SnapshotAccess[] Ancestors { get; init; } = [];
}
public sealed record SnapshotLock(string Expression, int Flags);
public sealed record ObjectSnapshot(
	string Id, int SchemaVersion, string ObjectId, string ObjectType, string CreatorAccount,
	string CreatorCharacter, long CreatedAt, string Description, int Retain,
	string Name, SnapshotAttribute[] Attributes, Dictionary<string, SnapshotLock> Locks,
	string[] Flags, string Digest)
{
	public string[] AbsentAttributes { get; init; } = [];
	public string[] AbsentLocks { get; init; } = [];
	public SnapshotSelection? RecoverySelection { get; init; }
	public string[] DefaultAttributes()
	{
		IEnumerable<string> names = RecoverySelection is { } recovery ? recovery.Attributes : Attributes.Select(a => a.Name);
		return names.Concat(AbsentAttributes).Distinct(StringComparer.Ordinal).ToArray();
	}
}
public sealed record SnapshotSelection(string[] Attributes, bool Locks = false, bool Flags = false, bool Name = false);
public sealed record SnapshotDifference(string Field, string Before, string After, bool FormattingChanged = false);
public sealed record SnapshotPreview(string SnapshotId, string ObjectId, string Token, SnapshotSelection Selection, SnapshotDifference[] Changes);
public sealed record SnapshotRestoreResult(bool Completed, string RecoverySnapshotId, string? Error);
public sealed record SnapshotResolution(string SnapshotId, string AccountId, string Character, long ResolvedAt);
public sealed record SnapshotHistory(ObjectSnapshot[] Snapshots, string? PendingRecoveryId = null, string? LastRestoreError = null)
{
	public SnapshotResolution? LastResolution { get; init; }
}
