namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Specifies which database provider to use.
/// </summary>
public enum DatabaseProvider
{
	/// <summary>
	/// ArangoDB - the default graph database provider.
	/// </summary>
	ArangoDB,

	/// <summary>
	/// Memgraph - a Cypher-compatible graph database using the Bolt protocol.
	/// </summary>
	Memgraph,

	/// <summary>
	/// SurrealDB - a multi-model database embedded in-process; RocksDB on disk in production, in-memory in tests.
	/// </summary>
	SurrealDB,

	/// <summary>
	/// LMDB embedded in-process through Lightning.NET; one directory per world.
	/// </summary>
	Lightning
}
