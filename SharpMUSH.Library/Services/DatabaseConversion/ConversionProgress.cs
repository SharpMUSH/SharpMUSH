namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Represents the current progress of a PennMUSH database conversion operation.
/// </summary>
public class ConversionProgress
{
	public int TotalObjects { get; init; }
	public int ProcessedObjects { get; init; }
	public int PlayersCreated { get; init; }
	public int RoomsCreated { get; init; }
	public int ThingsCreated { get; init; }
	public int ExitsCreated { get; init; }
	public int AttributesCreated { get; init; }
	public int LocksCreated { get; init; }
	public string CurrentPhase { get; init; } = string.Empty;
	public double PercentageComplete { get; init; }
	public TimeSpan ElapsedTime { get; init; }
	public TimeSpan? EstimatedTimeRemaining { get; init; }
	public List<string> RecentErrors { get; init; } = [];
	public List<string> RecentWarnings { get; init; } = [];
}
