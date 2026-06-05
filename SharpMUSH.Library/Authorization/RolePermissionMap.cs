namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Static mapping of portal roles to their atomic permissions.
/// Each role inherits all permissions from lower roles.
/// </summary>
public static class RolePermissionMap
{
	/// <summary>
	/// Gets all permissions for a given role.
	/// Higher roles inherit all permissions from lower roles.
	/// </summary>
	public static HashSet<Permission> GetPermissionsForRole(PortalRole role) =>
		role switch
		{
			PortalRole.God => GodPermissions(),
			PortalRole.Wizard => WizardPermissions(),
			PortalRole.Royalty => RoyaltyPermissions(),
			PortalRole.Player => PlayerPermissions(),
			PortalRole.Guest => GuestPermissions(),
			_ => GuestPermissions()
		};

	/// <summary>Guest role: minimal permissions, mostly read-only where allowed.</summary>
	private static HashSet<Permission> GuestPermissions() =>
	[
		// Guests can view some non-admin content but cannot access admin panel
	];

	/// <summary>Player role: basic character and account management.</summary>
	private static HashSet<Permission> PlayerPermissions() =>
	[
		..GuestPermissions(),
		Permission.ViewAdminPanel,
		Permission.EditLayout, // Can customize their own portal layout
	];

	/// <summary>Royalty role: mid-tier admin - account management, scenes, communication.</summary>
	private static HashSet<Permission> RoyaltyPermissions() =>
	[
		..PlayerPermissions(),
		// Account management
		Permission.ManageAccounts,
		Permission.CreateAccounts,
		Permission.EditAccounts,
		// Scene management
		Permission.CreateScene,
		Permission.EditScene,
		Permission.ViewPrivateScenes,
		Permission.DeleteScene,
		// Communication
		Permission.SendMail,
		Permission.ManageChannels,
		// Logging (read-only)
		Permission.ViewLogs,
		// Wiki
		Permission.EditWiki,
	];

	/// <summary>Wizard role: full admin access except system-level operations.</summary>
	private static HashSet<Permission> WizardPermissions() =>
	[
		..RoyaltyPermissions(),
		// Full account management
		Permission.DeleteAccounts,
		// Portal configuration
		Permission.ManagePortalSettings,
		Permission.ManagePackages,
		// Wiki management
		Permission.ManageWiki,
		// Logging (full access)
		Permission.ExportLogs,
	];

	/// <summary>God role: unrestricted superuser access to all functions.</summary>
	private static HashSet<Permission> GodPermissions() =>
	[
		..WizardPermissions(),
		Permission.ManageServer,
		Permission.ManageDatabase,
		Permission.SuperAdmin,
	];

	/// <summary>
	/// Checks if a role has a specific permission.
	/// </summary>
	public static bool HasPermission(PortalRole role, Permission permission)
	{
		var permissions = GetPermissionsForRole(role);
		return permissions.Contains(permission);
	}

	/// <summary>
	/// Checks if a role has all of the specified permissions.
	/// </summary>
	public static bool HasAllPermissions(PortalRole role, params Permission[] permissions)
	{
		var rolePermissions = GetPermissionsForRole(role);
		return permissions.All(p => rolePermissions.Contains(p));
	}

	/// <summary>
	/// Checks if a role has any of the specified permissions.
	/// </summary>
	public static bool HasAnyPermission(PortalRole role, params Permission[] permissions)
	{
		var rolePermissions = GetPermissionsForRole(role);
		return permissions.Any(p => rolePermissions.Contains(p));
	}
}
