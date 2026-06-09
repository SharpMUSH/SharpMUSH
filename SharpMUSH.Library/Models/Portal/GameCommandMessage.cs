namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Carries a command typed by a character in the web portal.
/// Published to NATS so the game engine can route it to the command pipeline.
/// </summary>
public record GameCommandMessage(
	string CharacterDbref,
	string Command,
	DateTimeOffset Timestamp);
