namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Specifies which database provider to use.
/// </summary>
public enum DatabaseProvider
{
	/// <summary>
	/// SurrealDB - a multi-model database embedded in-process; RocksDB on disk in production, in-memory in tests.
	/// </summary>
	SurrealDB,

	/// <summary>
	/// LMDB embedded in-process through Lightning.NET; one directory per world.
	/// </summary>
	Lightning
}
