namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Carries a room-scoped event to every character observing that room.
/// Serialised as JSON and forwarded by <c>NatsBridgeService</c> to the
/// room's SignalR group (<c>room:{dbref}</c>).
/// </summary>
public record RoomEventMessage(
	string RoomDbref,
	RoomEventType EventType,
	string ActorName,
	string Content);
