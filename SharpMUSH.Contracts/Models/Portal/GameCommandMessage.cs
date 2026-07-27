namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Carries a command typed by a character in the web portal.
/// Published to NATS so the game engine can route it to the command pipeline.
/// </summary>
/// <param name="CharacterDbref">
/// The issuing character's objid — <c>"#42:1700000000"</c>, the round-trip form of <c>DBRef</c>.
/// </param>
public record GameCommandMessage(
	string CharacterDbref,
	string Command,
	DateTimeOffset Timestamp);
