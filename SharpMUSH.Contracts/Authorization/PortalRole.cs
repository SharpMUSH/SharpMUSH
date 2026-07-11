namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Ordered privilege levels for the web portal. Higher values are more privileged.
/// </summary>
public enum PortalRole
{
	Guest = 0,
	Player = 10,
	Builder = 15,
	Royalty = 20,
	Wizard = 30,
	God = 40,
}
