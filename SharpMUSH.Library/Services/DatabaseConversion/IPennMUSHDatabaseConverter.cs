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
