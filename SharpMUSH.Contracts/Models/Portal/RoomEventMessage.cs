namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Carries a room-scoped event to every character observing that room.
/// Serialised as JSON and forwarded by <c>NatsBridgeService</c> to the
/// room's SignalR group (<c>room:{objid}</c>).
/// </summary>
/// <param name="RoomDbref">
/// The room's objid — <c>"#7:1700000000"</c>, the round-trip form of <c>DBRef</c>. A value that
/// does not parse is dropped by the bridge.
/// </param>
public record RoomEventMessage(
	string RoomDbref,
	RoomEventType EventType,
	string ActorName,
	string Content);
