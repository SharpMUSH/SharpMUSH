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
	/// SurrealDB - a multi-model database using embedded in-memory mode.
	/// </summary>
	SurrealDB
}
