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

/// <summary>
/// Result of a database conversion operation
/// </summary>
public record ConversionResult
{
	public int PlayersConverted { get; init; }
	public int RoomsConverted { get; init; }
	public int ThingsConverted { get; init; }
	public int ExitsConverted { get; init; }
	public int AttributesConverted { get; init; }
	public int LocksConverted { get; init; }
	public List<string> Errors { get; init; } = [];
	public List<string> Warnings { get; init; } = [];
	public TimeSpan Duration { get; init; }

	public int TotalObjects => PlayersConverted + RoomsConverted + ThingsConverted + ExitsConverted;

	public bool IsSuccessful => Errors.Count == 0;
}
