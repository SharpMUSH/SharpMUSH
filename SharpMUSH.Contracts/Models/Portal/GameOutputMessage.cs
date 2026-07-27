namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Carries output from the game engine to a specific connected character.
/// Serialised as JSON and forwarded by <c>NatsBridgeService</c> to the
/// character's SignalR group (<c>char:{objid}</c>).
/// </summary>
/// <param name="CharacterDbref">
/// The recipient character's objid — <c>"#42:1700000000"</c>, the round-trip form of
/// <c>DBRef</c>. A bare <c>"#42"</c> parses but is recycle-unsafe. <see langword="null"/> marks a
/// server-wide broadcast with no single recipient; the bridge drops such a message rather than
/// forwarding it to a group nobody is in, as do values that do not parse.
/// </param>
public record GameOutputMessage(
	string? CharacterDbref,
	string Content,
	DateTimeOffset Timestamp,
	MessageType MessageType);
