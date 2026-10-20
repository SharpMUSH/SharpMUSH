namespace SharpMUSH.Database.Lightning.Migration;

internal enum WikiCategoryMigrationPhase : byte
{
	Clearing = 1,
	Building = 2,
	Complete = 3
}

/// <summary>
/// Borrows checkpoint fields. The caller must retain their immutable storage and its budget lease.
/// This representation does not grant migration or readiness authority.
/// </summary>
internal readonly ref struct WikiCategoryMigrationCheckpoint(
	WikiCategoryMigrationPhase phase, ulong revision, ulong clearedRows, ulong completedPages,
	ulong categoryPuts, ReadOnlySpan<byte> lastClearedKey, ReadOnlySpan<byte> lastCompletedSourceKey,
	ReadOnlySpan<byte> currentSourceKey, ReadOnlySpan<byte> currentSourceHash,
	ulong nextCategoryOrdinal, long completionUnixMs)
{
	public WikiCategoryMigrationPhase Phase { get; } = phase;
	public ulong Revision { get; } = revision;
	public ulong ClearedRows { get; } = clearedRows;
	public ulong CompletedPages { get; } = completedPages;
	public ulong CategoryPuts { get; } = categoryPuts;
	public ReadOnlySpan<byte> LastClearedKey { get; } = lastClearedKey;
	public ReadOnlySpan<byte> LastCompletedSourceKey { get; } = lastCompletedSourceKey;
	public ReadOnlySpan<byte> CurrentSourceKey { get; } = currentSourceKey;
	public ReadOnlySpan<byte> CurrentSourceHash { get; } = currentSourceHash;
	public ulong NextCategoryOrdinal { get; } = nextCategoryOrdinal;
	public long CompletionUnixMs { get; } = completionUnixMs;
}
