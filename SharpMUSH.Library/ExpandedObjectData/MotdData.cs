namespace SharpMUSH.Library.ExpandedObjectData;

/// <summary>
/// Data class for storing Message of the Day (MOTD) strings.
/// These are temporary announcements shown to players on connect.
/// </summary>
[Serializable]
public record MotdData(
	string? ConnectMotd,
	string? WizardMotd,
	string? DownMotd,
	string? FullMotd
) : AbstractExpandedData
{
	/// <summary>
	/// Default constructor with all null values.
	/// </summary>
	public MotdData() : this(null, null, null, null) { }
}
