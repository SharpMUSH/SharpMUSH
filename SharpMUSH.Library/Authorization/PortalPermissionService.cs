using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Portal RBAC permission service implementation.
/// Derives portal roles from account characters and checks permissions.
/// </summary>
public class PortalPermissionService(IAccountService accountService) : IPortalPermissionService
{
	/// <summary>
	/// Gets the portal role for a given account.
	/// Derives the highest role from all linked character flags.
	/// </summary>
	public async ValueTask<PortalRole> GetAccountRoleAsync(string accountId, CancellationToken ct = default)
	{
		var characters = await accountService.GetCharactersAsync(accountId, ct);

		if (characters.Count == 0)
		{
			return PortalRole.Guest;
		}

		// Collect all flags from all characters
		var allFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var isGodCharacter = false;

		foreach (var character in characters)
		{
			// Check if this is the God character (object #1)
			if (character.Object?.DBRef.Id == 1)
			{
				isGodCharacter = true;
			}

			// Collect flag names from the character (Flags is Lazy<IAsyncEnumerable<SharpObjectFlag>>)
			await foreach (var flagObj in character.Object.Flags.Value.WithCancellation(ct))
			{
				allFlags.Add(flagObj.Name);
			}
		}

		return GetRoleFromFlags(allFlags, isGodCharacter);
	}

	/// <summary>
	/// Derives a portal role from character flags.
	/// </summary>
	public PortalRole GetRoleFromFlags(IEnumerable<string> characterFlags, bool isGodCharacter = false)
	{
		var flagSet = new HashSet<string>(characterFlags, StringComparer.OrdinalIgnoreCase);

		// God always gets God role
		if (isGodCharacter || flagSet.Contains("GOD"))
		{
			return PortalRole.God;
		}

		// Wizard role
		if (flagSet.Contains("WIZARD"))
		{
			return PortalRole.Wizard;
		}

		// Royalty role
		if (flagSet.Contains("ROYALTY"))
		{
			return PortalRole.Royalty;
		}

		// If any character exists, they're at least a Player
		// (This is checked by the caller; if there are no characters, this shouldn't be called with non-empty flags)
		if (flagSet.Count > 0)
		{
			return PortalRole.Player;
		}

		return PortalRole.Guest;
	}

	/// <summary>
	/// Checks if a role has a specific permission.
	/// </summary>
	public bool HasPermission(PortalRole role, Permission permission)
		=> RolePermissionMap.HasPermission(role, permission);

	/// <summary>
	/// Checks if a role has all of the specified permissions.
	/// </summary>
	public bool HasAllPermissions(PortalRole role, params Permission[] permissions)
		=> RolePermissionMap.HasAllPermissions(role, permissions);

	/// <summary>
	/// Checks if a role has any of the specified permissions.
	/// </summary>
	public bool HasAnyPermission(PortalRole role, params Permission[] permissions)
		=> RolePermissionMap.HasAnyPermission(role, permissions);

	/// <summary>
	/// Gets all permissions for a given role.
	/// </summary>
	public HashSet<Permission> GetPermissionsForRole(PortalRole role)
		=> RolePermissionMap.GetPermissionsForRole(role);
}
