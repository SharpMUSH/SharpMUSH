namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// A snapshot of <see cref="LightningStore.Usage"/>: the map ceiling and page counts in LMDB's own terms.
/// </summary>
/// <param name="MapSize">The map size in bytes — the most the file may grow to.</param>
/// <param name="PageSize">Bytes per page.</param>
/// <param name="FilePages">Pages the file has reached (last page number plus one).</param>
/// <param name="UsedPages">Pages holding live data, meta pages included.</param>
public readonly record struct LightningEnvironmentUsage(long MapSize, long PageSize, long FilePages, long UsedPages)
{
	/// <summary>Pages inside the file holding nothing live, reusable by later writes.</summary>
	public long FreePages => Math.Max(0, FilePages - UsedPages);

	/// <summary>
	/// The size a copy will be: a compacting copy writes only live pages; a plain one copies the file up
	/// to the last page it has reached.
	/// </summary>
	public long CopyBytes(bool compact) => (compact ? UsedPages : FilePages) * PageSize;
}
