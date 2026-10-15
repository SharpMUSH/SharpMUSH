namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Service interface for converting PennMUSH databases to SharpMUSH format.
/// </summary>
public interface IPennMUSHDatabaseConverter
{
	/// <summary>
	/// Convert a PennMUSH database file to SharpMUSH objects and store in the database.
	/// </summary>
	/// <param name="databaseFilePath">Path to the PennMUSH database file</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Conversion statistics</returns>
	Task<ConversionResult> ConvertDatabaseAsync(string databaseFilePath, CancellationToken cancellationToken = default);

	/// <summary>
	/// Convert a parsed PennMUSH database to SharpMUSH objects and store in the database.
	/// </summary>
	/// <param name="pennDatabase">Parsed PennMUSH database</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Conversion statistics</returns>
	Task<ConversionResult> ConvertDatabaseAsync(PennMUSHDatabase pennDatabase, CancellationToken cancellationToken = default);

	/// <summary>
	/// Convert a PennMUSH database file to SharpMUSH objects with progress reporting.
	/// </summary>
	/// <param name="databaseFilePath">Path to the PennMUSH database file</param>
	/// <param name="progress">Progress reporter for real-time updates</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Conversion statistics</returns>
	Task<ConversionResult> ConvertDatabaseAsync(
		string databaseFilePath,
		IProgress<ConversionProgress> progress,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Convert a PennMUSH database file, and its maildb when one is given, with progress reporting.
	/// </summary>
	/// <param name="databaseFilePath">Path to the PennMUSH database file</param>
	/// <param name="mailDatabaseFilePath">Path to the PennMUSH maildb, or null to import no mail data</param>
	/// <param name="progress">Progress reporter for real-time updates</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Conversion statistics</returns>
	Task<ConversionResult> ConvertDatabaseAsync(
		string databaseFilePath,
		string? mailDatabaseFilePath,
		IProgress<ConversionProgress> progress,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Convert a PennMUSH database file, and its maildb and chatdb when they are given, with progress reporting.
	/// </summary>
	/// <param name="databaseFilePath">Path to the PennMUSH database file</param>
	/// <param name="mailDatabaseFilePath">Path to the PennMUSH maildb, or null to import no mail data</param>
	/// <param name="chatDatabaseFilePath">Path to the PennMUSH chatdb, or null to import no channels</param>
	/// <param name="progress">Progress reporter for real-time updates</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Conversion statistics</returns>
	Task<ConversionResult> ConvertDatabaseAsync(
		string databaseFilePath,
		string? mailDatabaseFilePath,
		string? chatDatabaseFilePath,
		IProgress<ConversionProgress> progress,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Convert a database and the files that came with it, with progress reporting: a PennMUSH outdb with its
	/// maildb and chatdb, a RhostMUSH flatfile with its mail dumps, or a TinyMUX flatfile with its mail.db and
	/// comsys.db, in the format
	/// <see cref="DatabaseImportFiles.Format"/> names or, left on <see cref="DatabaseImportFormat.Auto"/>, the one
	/// the database's first lines show. The path overloads above are this with <see cref="DatabaseImportFormat.Auto"/>.
	/// </summary>
	/// <param name="files">The database, its side files and its format</param>
	/// <param name="progress">Progress reporter for real-time updates</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Conversion statistics</returns>
	Task<ConversionResult> ConvertDatabaseAsync(
		DatabaseImportFiles files,
		IProgress<ConversionProgress> progress,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Convert a parsed PennMUSH database to SharpMUSH objects with progress reporting.
	/// </summary>
	/// <param name="pennDatabase">Parsed PennMUSH database</param>
	/// <param name="progress">Progress reporter for real-time updates</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Conversion statistics</returns>
	Task<ConversionResult> ConvertDatabaseAsync(
		PennMUSHDatabase pennDatabase,
		IProgress<ConversionProgress> progress,
		CancellationToken cancellationToken = default);
}
