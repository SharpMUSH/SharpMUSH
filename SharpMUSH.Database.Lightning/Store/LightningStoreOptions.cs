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

	/// <summary>See <see cref="LightningSyncMode"/>. Defaults to the durable one.</summary>
	public LightningSyncMode Sync { get; init; } = LightningSyncMode.Full;

	/// <summary>How long a commit may sit unflushed under <see cref="LightningSyncMode.Periodic"/>; the
	/// most a power failure can lose in that mode. Ignored by the other modes.</summary>
	public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The most jobs the writer thread folds into one transaction. Jobs that queue up while a commit is
	/// in flight are taken together and committed once — one sync for all of them — so this bounds how
	/// long a single commit can grow under a burst. It does not delay anything: a lone job still commits
	/// on its own, immediately.
	/// </summary>
	public int MaxBatch { get; init; } = 64;

	/// <summary>Parses the <c>SHARPMUSH_LIGHTNING_SYNC</c> setting: <c>full</c>, <c>nometasync</c> or
	/// <c>periodic</c>, case-insensitively. False for anything else, including empty.</summary>
	public static bool TryParseSyncMode(string? setting, out LightningSyncMode mode)
	{
		switch (setting?.Trim().ToLowerInvariant())
		{
			case "full": mode = LightningSyncMode.Full; return true;
			case "nometasync": mode = LightningSyncMode.NoMetaSync; return true;
			case "periodic": mode = LightningSyncMode.Periodic; return true;
			default: mode = LightningSyncMode.Full; return false;
		}
	}
}
