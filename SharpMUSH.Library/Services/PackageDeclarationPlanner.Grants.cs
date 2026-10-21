using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

public static partial class PackageDeclarationPlanner
{
	/// <summary>
	/// Plans the <c>grants:</c> block (format 1.15): each grant allows the permission on a role that leaves it unset. A
	/// role that already allows or denies it is left alone and the grant is not the package's; one the package allowed
	/// and the game has since cleared stays cleared. A grant the package drops is cleared where the role still allows it.
	/// The edited roles join <paramref name="roleWrites"/>, so a permission the package drops in the same apply is judged
	/// against the roles as they will stand.
	/// </summary>
	/// <returns>The grants the package owns afterwards.</returns>
	private static List<PackageGrantSpec> PlanGrants(PackageDeclarations declared, PackageDeclarations owned,
		PackageDeclarationLiveState live, IReadOnlySet<string> usable, List<SharpRole> roleWrites,
		List<PackageDeclarationChange> changes, long now)
	{
		var edited = new Dictionary<string, SharpRole>(StringComparer.Ordinal);
		SharpRole Edit(SharpRole role)
		{
			if (!edited.TryGetValue(role.Slug, out var copy))
			{
				copy = edited[role.Slug] = Copy(role, now);
			}

			return copy;
		}

		var ownedGrants = new List<PackageGrantSpec>();
		foreach (var grant in declared.RoleGrants)
		{
			var name = $"{grant.Role}: {grant.Permission}";
			var baseline = owned.RoleGrants.Contains(grant);
			if (live.Roles.FirstOrDefault(r => r.Slug == grant.Role) is not { } role)
			{
				changes.Add(new(PackageDeclarationKind.Grant, name, PackageDeclarationAction.Blocked,
					$"There is no role named {grant.Role}. Create it first with @role/create."));
				continue;
			}

			if (!usable.Contains(grant.Permission))
			{
				changes.Add(new(PackageDeclarationKind.Grant, name, PackageDeclarationAction.Blocked,
					$"No permission named {grant.Permission}."));
				continue;
			}

			switch (role.Permissions.GetValueOrDefault(grant.Permission, PermissionState.Inherit), baseline)
			{
				case (PermissionState.Inherit, false):
					Edit(role).Permissions[grant.Permission] = PermissionState.Allow;
					ownedGrants.Add(grant);
					changes.Add(new(PackageDeclarationKind.Grant, name, PackageDeclarationAction.Create, $"The {role.Name} role allows it."));
					break;
				case (PermissionState.Inherit, true):
					ownedGrants.Add(grant);
					changes.Add(new(PackageDeclarationKind.Grant, name, PackageDeclarationAction.NoChange,
						"The game cleared it, so it stays cleared."));
					break;
				case (PermissionState.Allow, true):
					ownedGrants.Add(grant);
					changes.Add(new(PackageDeclarationKind.Grant, name, PackageDeclarationAction.NoChange));
					break;
				case (var state, _):
					changes.Add(new(PackageDeclarationKind.Grant, name, PackageDeclarationAction.Existing,
						$"The {role.Name} role already {(state == PermissionState.Allow ? "allows" : "denies")} it; it is left as it is."));
					break;
			}
		}

		foreach (var grant in owned.RoleGrants.Where(g => !declared.RoleGrants.Contains(g)))
		{
			if (live.Roles.FirstOrDefault(r => r.Slug == grant.Role) is { } role
				&& role.Permissions.GetValueOrDefault(grant.Permission, PermissionState.Inherit) == PermissionState.Allow)
			{
				Edit(role).Permissions.Remove(grant.Permission);
				changes.Add(new(PackageDeclarationKind.Grant, $"{grant.Role}: {grant.Permission}", PackageDeclarationAction.Remove,
					$"The {role.Name} role no longer allows it."));
			}
		}

		roleWrites.AddRange(edited.Values);
		return ownedGrants;
	}

	/// <summary>A copy of <paramref name="role"/> with its own permissions to edit.</summary>
	private static SharpRole Copy(SharpRole role, long now) => new()
	{
		Id = role.Id,
		Slug = role.Slug,
		Name = role.Name,
		Category = role.Category,
		Color = role.Color,
		Priority = role.Priority,
		IsSystem = role.IsSystem,
		Permissions = new Dictionary<string, PermissionState>(role.Permissions, StringComparer.Ordinal),
		CreatedAt = role.CreatedAt,
		UpdatedAt = now
	};
}
