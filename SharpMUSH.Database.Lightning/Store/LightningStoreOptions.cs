namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// Environment settings. <see cref="PageSize"/> applies only when the environment is created; LMDB
/// reads it back from an existing file. <see cref="MapSize"/> is the file-size ceiling, not RAM: it is
/// sparse on Linux and macOS and grows incrementally on Windows with LMDB 1.0.
/// </summary>
public sealed record LightningStoreOptions
{
	public required string Path { get; init; }
	public long MapSize { get; init; } = 64L << 30;
	public int MaxReaders { get; init; } = 256;
	public int PageSize { get; init; } = 16384;
	/// <summary>
	/// How many named sub-databases the environment may hold. The catalogue in <c>Tables</c> is 57 of
	/// them, and the rest is headroom for the tables storage PLUGINS open through the accessor — the
	/// Scene plugin alone opens eight. LMDB fixes this at environment-open time and refuses the open of
	/// the table that goes over with <c>MDB_DBS_FULL</c>, so the ceiling has to lead the schema rather
	/// than track it.
	/// </summary>
	public int MaxDatabases { get; init; } = 128;
}
