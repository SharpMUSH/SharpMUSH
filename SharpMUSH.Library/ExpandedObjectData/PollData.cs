namespace SharpMUSH.Library.ExpandedObjectData;

/// <summary>
/// Data class for storing the poll/doing message shown at the top of WHO/DOING.
/// </summary>
[Serializable]
public record PollData : AbstractExpandedData
{
	public string? Message { get; init; }
	
	/// <summary>
	/// Default constructor with null message.
	/// </summary>
	public PollData() : this((string?)null) { }
	
	/// <summary>
	/// Constructor with message.
	/// </summary>
	public PollData(string? message)
	{
		Message = message;
	}
}
