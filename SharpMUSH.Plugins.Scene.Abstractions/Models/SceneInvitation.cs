namespace SharpMUSH.Plugins.Scene.Models;

/// <summary>A player invited to a scene who has not joined it yet.</summary>
/// <param name="PlayerDbref">The player's dbref, such as <c>#42</c>.</param>
/// <param name="PlayerName">The player's name as it is now; empty when the object has no name.</param>
public record SceneInvitation(string PlayerDbref, string PlayerName);
