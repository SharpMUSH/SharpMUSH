namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Classifies an output message from the game engine to a connected client.
/// </summary>
public enum MessageType
{
	Normal,
	System,
	Error,
	Whisper,
}
