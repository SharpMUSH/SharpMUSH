namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Carries a room-scoped event to authorized observers of that room.
/// Serialised as JSON and forwarded by <c>NatsBridgeService</c> to the
/// recipient-aware room dispatcher. Reality-enabled delivery requires a full actor identity.
/// </summary>
/// <param name="RoomDbref">
/// The room's objid — <c>"#7:1700000000"</c>, the round-trip form of <c>DBRef</c>. A value that
/// does not parse is dropped by the bridge.
/// </param>
[method: System.Text.Json.Serialization.JsonConstructor]
public record RoomEventMessage(
	string RoomDbref,
	RoomEventType EventType,
	string ActorName,
	string Content)
{
	/// <summary>
	/// The source object's full objid. Reality-enabled delivery drops absent or bare identities;
	/// ActorName is display text and never identifies authority.
	/// </summary>
	public string? ActorDbref { get; init; }

	public RoomEventMessage(string RoomDbref, RoomEventType EventType, string ActorName,
		string Content, string? ActorDbref) : this(RoomDbref, EventType, ActorName, Content)
	{
		this.ActorDbref = ActorDbref;
	}
}
