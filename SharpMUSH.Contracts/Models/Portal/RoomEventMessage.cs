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
/// <param name="ActorDbref">
/// The source object's full objid. Required when reality layers are enabled; absent or bare
/// identities are dropped in enabled mode. ActorName is display text and never identifies authority.
/// </param>
public record RoomEventMessage(
	string RoomDbref,
	RoomEventType EventType,
	string ActorName,
	string Content,
	string? ActorDbref = null);
