namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Portal RBAC roles for web administration.
/// Ordered from least to most privileged; higher roles inherit all permissions of lower roles.
/// </summary>
public enum PortalRole
{
	/// <summary>Unauthenticated user or player with no special permissions.</summary>
	Guest = 0,

	/// <summary>Regular authenticated player.</summary>
	Player = 1,

	/// <summary>Royalty - mid-tier admin permissions.</summary>
	Royalty = 2,

	/// <summary>Wizard - full admin permissions.</summary>
	Wizard = 3,

	/// <summary>God - superuser, unrestricted access.</summary>
	God = 4
}
