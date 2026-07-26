namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Presence classes carried on a connection's <c>PresenceClass</c> metadata. <see cref="Play"/> is a
/// normal interactive session; <see cref="Portal"/> is a background query connection the web portal
/// opens for out-of-band lookups, which must not make a character appear connected to mortal viewers
/// (only wizards see portal-class sessions, mirroring how DARK sessions are treated).
/// </summary>
public static class PresenceClasses
{
	public const string Play = "play";
	public const string Portal = "portal";
}
