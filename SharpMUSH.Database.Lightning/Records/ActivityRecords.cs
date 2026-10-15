namespace SharpMUSH.Database.Lightning.Records;

/// <summary>One character's poses on one UTC day (see <c>LightningDatabase.Activity.cs</c>).</summary>
public sealed record ActivityPlayerRecord
{
	public int Poses { get; init; }
	public int Hours { get; init; }
}

/// <summary>One room's poses, scenes and posers on one UTC day (see <c>LightningDatabase.Activity.cs</c>).</summary>
public sealed record ActivityRoomRecord
{
	public int Poses { get; init; }
	public int Scenes { get; init; }
	public long[] Posers { get; init; } = [];
}
