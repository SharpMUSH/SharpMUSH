namespace SharpMUSH.Library;

/// <summary>
/// Represents a staging area for safe database import operations.
/// The staging database is a separate, isolated database instance that can be
/// populated and validated before being promoted to the live database.
///
/// For providers that support named databases (ArangoDB, SurrealDB), this creates
/// a parallel database. For single-database providers (Memgraph), this creates a
/// backup of the current state and works in-place with rollback capability.
/// </summary>
public interface IStagingDatabase : ISharpDatabase, IAsyncDisposable
{
	/// <summary>
	/// The unique identifier for this staging area.
	/// </summary>
	string StagingId { get; }

	/// <summary>
	/// Promotes this staging database to become the live database.
	/// For multi-database providers: swaps the staging DB into the live slot and drops the old one.
	/// For single-database providers: commits the current state and discards the backup.
	/// After this call, the staging instance is no longer valid.
	/// </summary>
	Task PromoteToLiveAsync(CancellationToken ct = default);

	/// <summary>
	/// Aborts the staging operation and cleans up.
	/// For multi-database providers: drops the staging database, live is untouched.
	/// For single-database providers: restores from backup.
	/// This is also called by DisposeAsync if PromoteToLiveAsync was never called.
	/// </summary>
	Task AbortAsync(CancellationToken ct = default);

	/// <summary>
	/// Whether PromoteToLiveAsync has been called successfully.
	/// </summary>
	bool IsPromoted { get; }
}
