namespace SharpMUSH.Database.Lightning.Records;

/// <summary>Mirrors <c>SurrealDatabase.ServerStateDbRecord</c>; a single fixed-key row for the whole game.</summary>
public sealed record ServerStateRecord
{
	public bool SetupCompleted { get; init; }
}
