using System.Text.RegularExpressions;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>categories:</c>, <c>permissions:</c>, and <c>roles:</c> blocks (format 1.2).</summary>
public partial class PackageManifestService
{
	private static readonly IReadOnlySet<string> KnownCategoryKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "description" };

	private static readonly IReadOnlySet<string> KnownPermissionKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "category", "description" };

	private static readonly IReadOnlySet<string> KnownRoleKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"slug", "name", "category", "color", "priority", "permissions"
	};

	[GeneratedRegex("^#[0-9a-fA-F]{6}$")]
	private static partial Regex RoleColorRegex();

	/// <summary>The highest priority a package role may take: one below the wizard role, so a wizard can always manage it.</summary>
	public static int MaxPackageRolePriority => (int)PortalRole.Wizard - 1;

	private static PackageDeclarations ReadDeclarations(
		Dictionary<string, object?> doc, PackageKind kind, List<PackageManifestIssue> issues)
	{
		if (kind == PackageKind.Managed)
		{
			foreach (var key in new[] { "categories", "permissions", "roles" }.Where(doc.ContainsKey))
			{
				issues.Add(PackageManifestIssue.Error(key,
					$"A managed package cannot declare '{key}'; declare them in a softcode package it depends on."));
			}

			return PackageDeclarations.None;
		}

		var (roleCategories, permissionCategories) = ReadCategories(doc, issues);
		var permissions = ReadPermissions(doc, issues);
		var roles = ReadRoles(doc, issues);
		return new PackageDeclarations(roleCategories, permissionCategories, permissions, roles);
	}

	private static (IReadOnlyList<PackageCategorySpec> Roles, IReadOnlyList<PackageCategorySpec> Permissions) ReadCategories(
		Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("categories", out var node) || node is null)
		{
			return ([], []);
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("categories", "'categories' must be a mapping with 'roles' and/or 'permissions' lists."));
			return ([], []);
		}

		var lists = Normalize(map);
		foreach (var key in lists.Keys.Where(k => k is not ("roles" or "permissions")))
		{
			issues.Add(PackageManifestIssue.Warning($"categories.{key}", $"Unknown key '{key}' is ignored."));
		}

		return (ReadCategoryList(lists, "roles", issues), ReadCategoryList(lists, "permissions", issues));
	}

	private static IReadOnlyList<PackageCategorySpec> ReadCategoryList(
		Dictionary<string, object?> lists, string key, List<PackageManifestIssue> issues)
	{
		var result = new List<PackageCategorySpec>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (entry, path) in Entries(lists, key, $"categories.{key}", KnownCategoryKeys, issues))
		{
			var name = (entry.GetValueOrDefault("name") as string)?.Trim() ?? "";
			if (!Categories.IsValidName(name) || name.Length == 0)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", Categories.NameRule));
				continue;
			}

			if (!seen.Add(name))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", $"Duplicate category '{name}'."));
				continue;
			}

			var description = (entry.GetValueOrDefault("description") as string)?.Trim() ?? "";
			if (description.Length is 0 or > Categories.MaxDescriptionLength || description.Any(char.IsControl))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.description",
					$"A category needs a description of 1 to {Categories.MaxDescriptionLength} characters on one line."));
				continue;
			}

			result.Add(new PackageCategorySpec(name, description));
		}

		return result;
	}

	private static IReadOnlyList<PackagePermissionSpec> ReadPermissions(
		Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		var result = new List<PackagePermissionSpec>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (entry, path) in Entries(doc, "permissions", "permissions", KnownPermissionKeys, issues))
		{
			var name = (entry.GetValueOrDefault("name") as string)?.Trim().ToLowerInvariant() ?? "";
			if (!CustomPermissions.IsValidName(name))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", PortalPermission.IsKnown(name)
					? $"{name} is a built-in permission; a package declares only its own."
					: $"'{name}' is not a custom permission name: two or more parts joined by '.', each of lowercase letters, digits or '_', such as scene.close; not under {string.Join(", ", CustomPermissions.ReservedPrefixes)}."));
				continue;
			}

			if (!seen.Add(name))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", $"Duplicate permission '{name}'."));
				continue;
			}

			var category = (entry.GetValueOrDefault("category") as string)?.Trim() ?? "";
			if (category.Length == 0)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.category", "A permission needs a category."));
				continue;
			}

			var description = (entry.GetValueOrDefault("description") as string)?.Trim() ?? "";
			if (description.Length > Authorization.RoleManagementService.MaxDescriptionLength)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.description",
					$"A description is at most {Authorization.RoleManagementService.MaxDescriptionLength} characters."));
				continue;
			}

			result.Add(new PackagePermissionSpec(name, category, description));
		}

		return result;
	}

	private static IReadOnlyList<PackageRoleSpec> ReadRoles(
		Dictionary<string, object?> doc, List<PackageManifestIssue> issues)
	{
		var result = new List<PackageRoleSpec>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (entry, path) in Entries(doc, "roles", "roles", KnownRoleKeys, issues))
		{
			var slug = (entry.GetValueOrDefault("slug") as string)?.Trim() ?? "";
			if (!RoleNames.IsValidShortName(slug))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.slug", RoleNames.ShortNameRule));
				continue;
			}

			if (BuiltInRoles.All.Any(r => r.Slug == slug))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.slug", $"{slug} is a system role; a package cannot declare it."));
				continue;
			}

			if (!seen.Add(slug))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.slug", $"Duplicate role '{slug}'."));
				continue;
			}

			var name = (entry.GetValueOrDefault("name") as string)?.Trim() ?? slug;
			if (!RoleNames.IsValidDisplayName(name))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", RoleNames.DisplayNameRule));
				continue;
			}

			var category = (entry.GetValueOrDefault("category") as string)?.Trim() ?? "";
			if (category.Length == 0)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.category", "A role needs a category."));
				continue;
			}

			var color = (entry.GetValueOrDefault("color") as string)?.Trim();
			if (color is not null && !RoleColorRegex().IsMatch(color))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.color", "A role colour is a hex colour such as #5aa9ff."));
				continue;
			}

			var priority = 1;
			if (entry.GetValueOrDefault("priority") is { } priorityNode
				&& (!int.TryParse(priorityNode.ToString(), out priority) || priority < 1 || priority > MaxPackageRolePriority))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.priority",
					$"A package role's priority is a whole number from 1 to {MaxPackageRolePriority}, below the wizard role."));
				continue;
			}

			if (ReadRolePermissions(entry, path, issues) is not { } permissions)
			{
				continue;
			}

			result.Add(new PackageRoleSpec(slug, name, category, color, priority, permissions));
		}

		return result;
	}

	private static IReadOnlyDictionary<string, PermissionState>? ReadRolePermissions(
		Dictionary<string, object?> entry, string path, List<PackageManifestIssue> issues)
	{
		var result = new Dictionary<string, PermissionState>(StringComparer.Ordinal);
		if (!entry.TryGetValue("permissions", out var node) || node is null)
		{
			return result;
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.permissions", "'permissions' must map each permission to allow or deny."));
			return null;
		}

		var ok = true;
		foreach (var (rawScope, rawState) in Normalize(map))
		{
			var scope = rawScope.Trim().ToLowerInvariant();
			if (!CustomPermissions.IsValidName(scope))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.permissions.{rawScope}", PortalPermission.IsKnown(scope)
					? $"{scope} is a built-in permission. A package role sets only custom permissions; grant built-in ones with @role."
					: $"'{rawScope}' is not a custom permission name."));
				ok = false;
				continue;
			}

			PermissionState? state = (rawState as string)?.Trim().ToLowerInvariant() switch
			{
				"allow" => PermissionState.Allow,
				"deny" => PermissionState.Deny,
				_ => null
			};
			if (state is not { } value)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.permissions.{rawScope}", "Must be allow or deny."));
				ok = false;
				continue;
			}

			result[scope] = value;
		}

		return ok ? result : null;
	}

	/// <summary>The mappings in the list <paramref name="key"/> of <paramref name="doc"/>, each with its document path; anything else is reported.</summary>
	private static IEnumerable<(Dictionary<string, object?> Entry, string Path)> Entries(
		Dictionary<string, object?> doc, string key, string path, IReadOnlySet<string> knownKeys, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue(key, out var node) || node is null)
		{
			yield break;
		}

		if (node is not List<object> list)
		{
			issues.Add(PackageManifestIssue.Error(path, $"'{key}' must be a list."));
			yield break;
		}

		for (var i = 0; i < list.Count; i++)
		{
			var itemPath = $"{path}[{i}]";
			if (list[i] is not Dictionary<object, object> raw)
			{
				issues.Add(PackageManifestIssue.Error(itemPath, "Entry must be a mapping."));
				continue;
			}

			var entry = Normalize(raw);
			foreach (var unknown in entry.Keys.Where(k => !knownKeys.Contains(k)))
			{
				issues.Add(PackageManifestIssue.Warning($"{itemPath}.{unknown}", $"Unknown key '{unknown}' is ignored."));
			}

			yield return (entry, itemPath);
		}
	}

	/// <summary>
	/// Checks the declarations against each other: a permission or role whose category the package does
	/// not declare must find it in the game at install, and a role's permission likewise; both are
	/// noted, not refused, since the game may well have them.
	/// </summary>
	private static void ValidateDeclarations(PackageDeclarations declared, List<PackageManifestIssue> issues)
	{
		var seeded = Categories.PermissionSeeds.Select(c => c.Name);
		var permissionCategories = declared.PermissionCategories.Select(c => c.Name).Concat(seeded).ToHashSet(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < declared.Permissions.Count; i++)
		{
			if (!permissionCategories.Contains(declared.Permissions[i].Category))
			{
				issues.Add(PackageManifestIssue.Warning($"permissions[{i}].category",
					$"'{declared.Permissions[i].Category}' is not declared under categories.permissions; the game must already have it."));
			}
		}

		var roleCategories = declared.RoleCategories.Select(c => c.Name).Concat(Categories.RoleSeeds.Select(c => c.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var permissions = declared.Permissions.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
		for (var i = 0; i < declared.Roles.Count; i++)
		{
			var role = declared.Roles[i];
			if (!roleCategories.Contains(role.Category))
			{
				issues.Add(PackageManifestIssue.Warning($"roles[{i}].category",
					$"'{role.Category}' is not declared under categories.roles; the game must already have it."));
			}

			foreach (var scope in role.Permissions.Keys.Where(scope => !permissions.Contains(scope)))
			{
				issues.Add(PackageManifestIssue.Warning($"roles[{i}].permissions.{scope}",
					$"{scope} is not declared under permissions; the game must already define it."));
			}
		}
	}
}
