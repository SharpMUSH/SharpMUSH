using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>The game's roles, permissions and categories, as a declaration plan reads them.</summary>
/// <param name="RoleCategories">Every role category.</param>
/// <param name="PermissionCategories">Every permission category.</param>
/// <param name="Permissions">Every custom permission.</param>
/// <param name="Roles">Every role.</param>
/// <param name="HeldRoles">The slugs, among the roles the package owns, that an account or object holds.</param>
/// <param name="OtherOwners">Items other installed packages own, keyed by kind and lowercase name, to the owning package.</param>
public sealed record PackageDeclarationLiveState(
	IReadOnlyList<RoleCategory> RoleCategories,
	IReadOnlyList<RoleCategory> PermissionCategories,
	IReadOnlyList<CustomPermission> Permissions,
	IReadOnlyList<SharpRole> Roles,
	IReadOnlySet<string> HeldRoles,
	IReadOnlyDictionary<(PackageDeclarationKind Kind, string Name), string> OtherOwners);

/// <summary>A declaration plan: what is shown, what is written, and what the package owns afterwards.</summary>
public sealed record PackageDeclarationPlan(
	IReadOnlyList<PackageDeclarationChange> Changes,
	IReadOnlyList<(CategoryKind Kind, RoleCategory Category)> CategoryWrites,
	IReadOnlyList<CustomPermission> PermissionWrites,
	IReadOnlyList<SharpRole> RoleWrites,
	IReadOnlyList<string> RoleRemovals,
	IReadOnlyList<string> PermissionRemovals,
	IReadOnlyList<(CategoryKind Kind, string Name)> CategoryRemovals,
	PackageDeclarations Owned)
{
	/// <summary>True when an item cannot be applied.</summary>
	public bool IsBlocked => Changes.Any(c => c.Action == PackageDeclarationAction.Blocked);
}

/// <summary>
/// The pure half of <see cref="IPackageDeclarationService"/>: diffs what a package declares against
/// what it owned at its last apply and what the game has now. See that interface for the rules.
/// </summary>
public static class PackageDeclarationPlanner
{
	private static readonly StringComparer NoCase = StringComparer.OrdinalIgnoreCase;

	public static PackageDeclarationPlan Plan(
		PackageDeclarations declared, PackageDeclarations? owned, PackageDeclarationLiveState live, long now)
	{
		owned ??= PackageDeclarations.None;
		var changes = new List<PackageDeclarationChange>();
		var categoryWrites = new List<(CategoryKind, RoleCategory)>();
		var permissionWrites = new List<CustomPermission>();
		var roleWrites = new List<SharpRole>();
		var ownedRoleCategories = new List<PackageCategorySpec>();
		var ownedPermissionCategories = new List<PackageCategorySpec>();
		var ownedPermissions = new List<PackagePermissionSpec>();
		var ownedRoles = new List<PackageRoleSpec>();

		// ── Categories ──────────────────────────────────────────────────────
		foreach (var kind in new[] { CategoryKind.Role, CategoryKind.Permission })
		{
			var liveList = kind == CategoryKind.Role ? live.RoleCategories : live.PermissionCategories;
			var keep = kind == CategoryKind.Role ? ownedRoleCategories : ownedPermissionCategories;
			var changeKind = kind == CategoryKind.Role ? PackageDeclarationKind.RoleCategory : PackageDeclarationKind.PermissionCategory;
			foreach (var spec in declared.Categories(kind))
			{
				var current = liveList.FirstOrDefault(c => NoCase.Equals(c.Name, spec.Name));
				var baseline = owned.Categories(kind).FirstOrDefault(c => NoCase.Equals(c.Name, spec.Name));
				if (current is null)
				{
					categoryWrites.Add((kind, new RoleCategory(spec.Name, spec.Description, now)));
					changes.Add(new(changeKind, spec.Name, PackageDeclarationAction.Create, baseline is null ? null : "It had been deleted, so it is made again."));
					keep.Add(spec);
				}
				else if (baseline is null)
				{
					changes.Add(new(changeKind, current.Name, PackageDeclarationAction.Existing, "The game already has it."));
				}
				else
				{
					var description = Merge(spec.Description, baseline.Description, current.Description);
					if (description != current.Description)
					{
						categoryWrites.Add((kind, current with { Description = description }));
						changes.Add(new(changeKind, current.Name, PackageDeclarationAction.Update, "New description."));
					}
					else
					{
						changes.Add(new(changeKind, current.Name, PackageDeclarationAction.NoChange));
					}

					keep.Add(spec);
				}
			}
		}

		bool CategoryExists(CategoryKind kind, string name)
			=> declared.Categories(kind).Any(c => NoCase.Equals(c.Name, name))
				|| (kind == CategoryKind.Role ? live.RoleCategories : live.PermissionCategories).Any(c => NoCase.Equals(c.Name, name));

		string CategoryName(CategoryKind kind, string name)
			=> (kind == CategoryKind.Role ? live.RoleCategories : live.PermissionCategories).FirstOrDefault(c => NoCase.Equals(c.Name, name))?.Name
				?? declared.Categories(kind).First(c => NoCase.Equals(c.Name, name)).Name;

		// ── Permissions ─────────────────────────────────────────────────────
		var usable = live.Permissions.Select(p => p.Scope).ToHashSet(StringComparer.Ordinal);
		foreach (var spec in declared.Permissions)
		{
			var current = live.Permissions.FirstOrDefault(p => p.Scope == spec.Name);
			var baseline = owned.Permissions.FirstOrDefault(p => p.Name == spec.Name);
			if (live.OtherOwners.TryGetValue((PackageDeclarationKind.Permission, spec.Name), out var other))
			{
				changes.Add(new(PackageDeclarationKind.Permission, spec.Name, PackageDeclarationAction.Blocked, $"It belongs to the {other} package."));
				continue;
			}

			if (current is not null && baseline is null)
			{
				changes.Add(new(PackageDeclarationKind.Permission, spec.Name, PackageDeclarationAction.Existing, "The game already defines it."));
				continue;
			}

			var category = baseline is null || current is null ? spec.Category : Merge(spec.Category, baseline.Category, current.Category);
			if (!CategoryExists(CategoryKind.Permission, category))
			{
				changes.Add(new(PackageDeclarationKind.Permission, spec.Name, PackageDeclarationAction.Blocked,
					$"There is no permission category named {category}. Declare it under categories.permissions, or create it first with @permission/category/create."));
				continue;
			}

			usable.Add(spec.Name);
			ownedPermissions.Add(spec);
			var permission = new CustomPermission(spec.Name, CategoryName(CategoryKind.Permission, category),
				current is null || baseline is null ? spec.Description : Merge(spec.Description, baseline.Description, current.Description),
				current?.CreatedAt ?? now);
			if (current is null)
			{
				permissionWrites.Add(permission);
				changes.Add(new(PackageDeclarationKind.Permission, spec.Name, PackageDeclarationAction.Create, baseline is null ? null : "It had been removed, so it is made again."));
			}
			else if (permission != current)
			{
				permissionWrites.Add(permission);
				changes.Add(new(PackageDeclarationKind.Permission, spec.Name, PackageDeclarationAction.Update));
			}
			else
			{
				changes.Add(new(PackageDeclarationKind.Permission, spec.Name, PackageDeclarationAction.NoChange));
			}
		}

		// ── Roles ───────────────────────────────────────────────────────────
		foreach (var spec in declared.Roles)
		{
			var current = live.Roles.FirstOrDefault(r => r.Slug == spec.Slug);
			var baseline = owned.Roles.FirstOrDefault(r => r.Slug == spec.Slug);
			if (live.OtherOwners.TryGetValue((PackageDeclarationKind.Role, spec.Slug), out var other))
			{
				changes.Add(new(PackageDeclarationKind.Role, spec.Slug, PackageDeclarationAction.Blocked, $"It belongs to the {other} package."));
				continue;
			}

			if (current is not null && baseline is null)
			{
				changes.Add(new(PackageDeclarationKind.Role, spec.Slug, PackageDeclarationAction.Existing,
					"The game already has this role; it is left as it is. Allow the package's permissions on it with @role if you want them."));
				continue;
			}

			var merged = current is null || baseline is null ? spec : MergeRole(spec, baseline, current);
			if (!CategoryExists(CategoryKind.Role, merged.Category))
			{
				changes.Add(new(PackageDeclarationKind.Role, spec.Slug, PackageDeclarationAction.Blocked,
					$"There is no role category named {merged.Category}. Declare it under categories.roles, or create it first with @role/category/create."));
				continue;
			}

			var unknown = spec.Permissions.Keys.Where(scope => !usable.Contains(scope)).ToArray();
			if (unknown.Length > 0)
			{
				changes.Add(new(PackageDeclarationKind.Role, spec.Slug, PackageDeclarationAction.Blocked,
					$"No permission named {string.Join(", ", unknown)}. Declare it under permissions, or define it first with @permission/define."));
				continue;
			}

			ownedRoles.Add(spec);
			var role = new SharpRole
			{
				Id = current?.Id,
				Slug = spec.Slug,
				Name = merged.Name,
				Category = CategoryName(CategoryKind.Role, merged.Category),
				Color = merged.Color,
				Priority = merged.Priority,
				IsSystem = false,
				Permissions = new Dictionary<string, PermissionState>(merged.Permissions),
				CreatedAt = current?.CreatedAt ?? now,
				UpdatedAt = now
			};
			if (current is null)
			{
				roleWrites.Add(role);
				changes.Add(new(PackageDeclarationKind.Role, spec.Slug, PackageDeclarationAction.Create, baseline is null ? null : "It had been deleted, so it is made again."));
			}
			else if (SameRole(role, current))
			{
				changes.Add(new(PackageDeclarationKind.Role, spec.Slug, PackageDeclarationAction.NoChange));
			}
			else
			{
				roleWrites.Add(role);
				changes.Add(new(PackageDeclarationKind.Role, spec.Slug, PackageDeclarationAction.Update));
			}
		}

		// ── What the package drops ──────────────────────────────────────────
		var roleRemovals = new List<string>();
		foreach (var baseline in owned.Roles.Where(b => declared.Roles.All(d => d.Slug != b.Slug) && live.Roles.Any(r => r.Slug == b.Slug)))
		{
			if (live.HeldRoles.Contains(baseline.Slug))
			{
				changes.Add(new(PackageDeclarationKind.Role, baseline.Slug, PackageDeclarationAction.Release, "Someone holds it, so it stays, and is no longer the package's."));
			}
			else
			{
				roleRemovals.Add(baseline.Slug);
				changes.Add(new(PackageDeclarationKind.Role, baseline.Slug, PackageDeclarationAction.Remove));
			}
		}

		// The roles as they will stand: the game's, less those removed, with this apply's writes in place.
		var rolesAfter = live.Roles
			.Where(r => !roleRemovals.Contains(r.Slug))
			.Select(r => roleWrites.FirstOrDefault(w => w.Slug == r.Slug) ?? r)
			.Concat(roleWrites.Where(w => live.Roles.All(r => r.Slug != w.Slug)))
			.ToList();

		var permissionRemovals = new List<string>();
		foreach (var baseline in owned.Permissions.Where(b => declared.Permissions.All(d => d.Name != b.Name) && live.Permissions.Any(p => p.Scope == b.Name)))
		{
			var setters = rolesAfter.Where(r => PermissionResolver.StateOf(r.Permissions, baseline.Name) != PermissionState.Inherit).Select(r => r.Slug).ToArray();
			if (setters.Length > 0)
			{
				changes.Add(new(PackageDeclarationKind.Permission, baseline.Name, PackageDeclarationAction.Release,
					$"The {string.Join(", ", setters)} role{(setters.Length == 1 ? "" : "s")} set{(setters.Length == 1 ? "s" : "")} it, so it stays, and is no longer the package's."));
			}
			else
			{
				permissionRemovals.Add(baseline.Name);
				changes.Add(new(PackageDeclarationKind.Permission, baseline.Name, PackageDeclarationAction.Remove,
					"Any account or object override of it goes with it."));
			}
		}

		var permissionsAfter = live.Permissions
			.Where(p => !permissionRemovals.Contains(p.Scope))
			.Select(p => permissionWrites.FirstOrDefault(w => w.Scope == p.Scope) ?? p)
			.Concat(permissionWrites.Where(w => live.Permissions.All(p => p.Scope != w.Scope)))
			.ToList();

		var categoryRemovals = new List<(CategoryKind, string)>();
		foreach (var kind in new[] { CategoryKind.Role, CategoryKind.Permission })
		{
			var liveList = kind == CategoryKind.Role ? live.RoleCategories : live.PermissionCategories;
			var changeKind = kind == CategoryKind.Role ? PackageDeclarationKind.RoleCategory : PackageDeclarationKind.PermissionCategory;
			var dropped = owned.Categories(kind)
				.Where(b => declared.Categories(kind).All(d => !NoCase.Equals(d.Name, b.Name)))
				.Select(b => liveList.FirstOrDefault(c => NoCase.Equals(c.Name, b.Name)))
				.OfType<RoleCategory>();
			foreach (var current in dropped)
			{
				var members = kind == CategoryKind.Role
					? rolesAfter.Where(r => NoCase.Equals(r.Category, current.Name)).Select(r => r.Slug).ToArray()
					: permissionsAfter.Where(p => NoCase.Equals(p.Category, current.Name)).Select(p => p.Scope).ToArray();
				if (members.Length > 0)
				{
					changes.Add(new(changeKind, current.Name, PackageDeclarationAction.Release,
						$"It still holds {string.Join(", ", members)}, so it stays, and is no longer the package's."));
				}
				else
				{
					categoryRemovals.Add((kind, current.Name));
					changes.Add(new(changeKind, current.Name, PackageDeclarationAction.Remove));
				}
			}
		}

		return new PackageDeclarationPlan(
			changes, categoryWrites, permissionWrites, roleWrites, roleRemovals, permissionRemovals, categoryRemovals,
			new PackageDeclarations(ownedRoleCategories, ownedPermissionCategories, ownedPermissions, ownedRoles));
	}

	/// <summary>The three-way merge of one field: the package's new value when it changed it, else the game's.</summary>
	private static T Merge<T>(T incoming, T baseline, T current) => EqualityComparer<T>.Default.Equals(incoming, baseline) ? current : incoming;

	/// <summary>A role as the package declares it now, merged against what it declared before and the game's copy.</summary>
	private static PackageRoleSpec MergeRole(PackageRoleSpec spec, PackageRoleSpec baseline, SharpRole current)
	{
		var permissions = new Dictionary<string, PermissionState>(current.Permissions, StringComparer.Ordinal);
		var changed = spec.Permissions.Keys.Union(baseline.Permissions.Keys)
			.Where(scope => spec.Permissions.GetValueOrDefault(scope, PermissionState.Inherit) != baseline.Permissions.GetValueOrDefault(scope, PermissionState.Inherit));
		foreach (var scope in changed)
		{
			var incoming = spec.Permissions.GetValueOrDefault(scope, PermissionState.Inherit);
			if (incoming == PermissionState.Inherit) permissions.Remove(scope);
			else permissions[scope] = incoming;
		}

		return new PackageRoleSpec(
			spec.Slug,
			Merge(spec.Name, baseline.Name, current.Name),
			Merge(spec.Category, baseline.Category, current.Category),
			Merge(spec.Color, baseline.Color, current.Color),
			Merge(spec.Priority, baseline.Priority, current.Priority),
			permissions);
	}

	private static bool SameRole(SharpRole a, SharpRole b)
		=> a.Name == b.Name && NoCase.Equals(a.Category, b.Category) && a.Color == b.Color && a.Priority == b.Priority
			&& a.Permissions.Count == b.Permissions.Count
			&& a.Permissions.All(p => b.Permissions.TryGetValue(p.Key, out var state) && state == p.Value);
}
