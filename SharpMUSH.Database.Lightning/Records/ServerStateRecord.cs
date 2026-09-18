namespace SharpMUSH.Database.Lightning.Records;

/// <summary>Game-wide server state; a single fixed-key row for the whole game.</summary>
public sealed record ServerStateRecord
{
	public bool SetupCompleted { get; init; }
}
