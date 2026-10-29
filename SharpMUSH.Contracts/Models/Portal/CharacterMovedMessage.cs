namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Tells a character's portal tabs that the character moved, by any means: walking, teleporting, being carried. The
/// World map follows them with it.
/// </summary>
/// <param name="Room">The objid of where the character now is.</param>
public record CharacterMovedMessage(string Room);
