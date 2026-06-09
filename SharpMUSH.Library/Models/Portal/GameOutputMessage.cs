namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Carries output from the game engine to a specific connected character.
/// Serialised as JSON and forwarded by <c>NatsBridgeService</c> to the
/// character's SignalR group (<c>char:{dbref}</c>).
/// </summary>
public record GameOutputMessage(
	string CharacterDbref,
	string Content,
	DateTimeOffset Timestamp,
	MessageType MessageType);
