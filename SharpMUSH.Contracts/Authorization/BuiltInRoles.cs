using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The undeletable built-in roles, one per <see cref="PortalRole"/>. Their slugs match the role
/// name (lower-cased), their priority matches the enum value, and their default permission grants
/// reproduce the legacy gating: God grants everything, Wizard grants every scope except
/// <c>server.admin</c>, and the authenticated member tiers (Player/Builder/Royalty) grant the
/// <see cref="Contributor"/> scopes — wiki read/create/edit and image upload — which preserves the
/// pre-RBAC "any logged-in user can contribute to the wiki" behavior while leaving delete and
/// moderation to staff. Built-in roles do NOT stack (an account holds exactly the role for its
/// derived tier), so each tier must grant everything it should be able to do. Seeded idempotently
/// at startup; admins may edit a built-in's permissions/priority but never delete or re-slug it.
/// </summary>
public static class BuiltInRoles
{
	/// <summary>
	/// Baseline scopes every authenticated member tier may exercise: read drafts, create/edit wiki
	/// pages, and upload images. Deliberately excludes delete and any moderation/admin scope.
	/// Declared before <see cref="All"/> so it is initialized before <see cref="Build"/> reads it.
	/// </summary>
	private static readonly string[] Contributor =
	[
		PortalPermission.WikiRead,
		PortalPermission.WikiCreate,
		PortalPermission.WikiEdit,
		PortalPermission.MediaUpload,
	];

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
				PortalRole.Royalty or PortalRole.Builder or PortalRole.Player => Contributor
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
