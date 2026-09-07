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
	public int MaxDatabases { get; init; } = 64;
}
