namespace SharpMUSH.Library;

/// <summary>
/// Provider lifecycle: schema migration, full wipe, and staging-database creation for imports.
/// </summary>
public interface IDatabaseLifecycle
{
	/// <summary>
	/// Initialize the Database, and Upgrade as needed.
	/// </summary>
	ValueTask Migrate(CancellationToken cancellationToken = default);

	/// <summary>
	/// Completely wipe the database and re-initialize it from scratch.
	/// WARNING: This is destructive and irreversible.
	/// </summary>
	ValueTask WipeDatabaseAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a staging database for safe import operations.
	/// The staging area is an isolated database that can be populated, validated,
	/// and then either promoted to live or aborted without affecting the current data.
	/// </summary>
	/// <returns>A staging database instance that must be disposed after use.</returns>
	Task<IStagingDatabase> CreateStagingAsync(CancellationToken ct = default);
}
