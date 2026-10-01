namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// A character's read marker. The key is the character's dbref and the scope; the record carries the
/// character's creation time, so a marker left by an earlier holder of a recycled dbref is not theirs.
/// </summary>
public sealed record ReadMarkerRecord
{
	public long CharacterCreationTime { get; init; }
	public string Scope { get; init; } = "";
	public long? LastReadId { get; init; }
	public long LastReadAtMs { get; init; }
}
