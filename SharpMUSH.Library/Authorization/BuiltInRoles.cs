using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The undeletable built-in roles, one per <see cref="PortalRole"/>. Their slugs match the role
/// name (lower-cased), their priority matches the enum value, and their default permission grants
/// reproduce the legacy gating exactly: Wizard grants every admin scope except <c>server.admin</c>,
/// God grants everything, lower roles grant nothing by default. Seeded idempotently at startup;
/// admins may edit a built-in's permissions/priority but never delete or re-slug it.
/// </summary>
public static class BuiltInRoles
{
	/// <summary>Seed templates for every built-in role (timestamps stamped at insert time).</summary>
	public static readonly IReadOnlyList<SharpRole> All = Build();

	/// <summary>The built-in role slug for a derived <see cref="PortalRole"/> (e.g. Wizard → "wizard").</summary>
	public static string SlugFor(PortalRole role) => role.ToString().ToLowerInvariant();

	private static IReadOnlyList<SharpRole> Build()
	{
		var roles = new List<SharpRole>();
		foreach (var role in Enum.GetValues<PortalRole>())
		{
			var permissions = role switch
			{
				PortalRole.God => PortalPermission.AllScopes
					.ToDictionary(s => s, _ => PermissionState.Allow),
				PortalRole.Wizard => PortalPermission.AllScopes
					.Where(s => s != PortalPermission.ServerAdmin)
					.ToDictionary(s => s, _ => PermissionState.Allow),
				_ => new Dictionary<string, PermissionState>()
			};

			roles.Add(new SharpRole
			{
				Slug = SlugFor(role),
				Name = role.ToString(),
				Priority = (int)role,
				IsSystem = true,
				Color = ColorFor(role),
				Permissions = permissions
			});
		}

		return roles;
	}

	private static string ColorFor(PortalRole role) => role switch
	{
		PortalRole.God => "#ffd166",
		PortalRole.Wizard => "#5aa9ff",
		PortalRole.Royalty => "#b39cff",
		PortalRole.Builder => "#6cde9a",
		PortalRole.Player => "#9aa3ab",
		_ => "#5f6870"
	};
}
