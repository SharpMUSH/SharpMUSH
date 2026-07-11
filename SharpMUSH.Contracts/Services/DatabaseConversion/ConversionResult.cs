namespace SharpMUSH.Library.Services.DatabaseConversion;

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
