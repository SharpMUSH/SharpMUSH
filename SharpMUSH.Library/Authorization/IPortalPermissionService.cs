namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Portal RBAC permission service interface.
/// Provides role derivation and permission checking for web portal admin access.
/// </summary>
public interface IPortalPermissionService
{
	/// <summary>
	/// Gets the portal role for a given account based on their linked character flags.
	/// Account role = highest role among all their linked characters.
	/// </summary>
	/// <remarks>
	/// Role hierarchy is determined by character flags:
	/// - GOD flag (or object #1) → God
	/// - WIZARD flag → Wizard
	/// - ROYALTY flag → Royalty
	/// - Any other player → Player
	/// - No linked characters or guest → Guest
	/// </remarks>
	ValueTask<PortalRole> GetAccountRoleAsync(string accountId, CancellationToken ct = default);

	/// <summary>
	/// Derives a portal role from character flags.
	/// Used internally and for testing; typically called via GetAccountRoleAsync.
	/// </summary>
	PortalRole GetRoleFromFlags(IEnumerable<string> characterFlags, bool isGodCharacter = false);

	/// <summary>
	/// Checks if a role has a specific permission.
	/// </summary>
	bool HasPermission(PortalRole role, Permission permission);

	/// <summary>
	/// Checks if a role has all of the specified permissions.
	/// </summary>
	bool HasAllPermissions(PortalRole role, params Permission[] permissions);

	/// <summary>
	/// Checks if a role has any of the specified permissions.
	/// </summary>
	bool HasAnyPermission(PortalRole role, params Permission[] permissions);

	/// <summary>
	/// Gets all permissions for a given role.
	/// </summary>
	HashSet<Permission> GetPermissionsForRole(PortalRole role);
}
