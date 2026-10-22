using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>grants:</c> block (format 1.15).</summary>
public partial class PackageManifestService
{
	/// <summary>The first format with the <c>grants:</c> block; below it the block is ignored.</summary>
	public static PackageFormatVersion GrantsFormat { get; } = new(1, 15);

	/// <summary>
	/// The permissions a package allows on roles the game already has: a mapping of role slug to a list of the
	/// package's own permissions, such as <c>wizard: [softcode.jobs.admin]</c>. A role the package declares sets its
	/// permissions under <c>roles:</c> instead, and a permission the package does not declare is the game's to hand out.
	/// </summary>
	private static IReadOnlyList<PackageGrantSpec> ReadGrants(Dictionary<string, object?> doc, PackageKind kind,
		PackageFormatVersion format, PackageDeclarations declared, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("grants", out var node) || node is null)
		{
			return [];
		}

		if (kind is PackageKind.Plugin or PackageKind.Ui)
		{
			issues.Add(PackageManifestIssue.Error("grants",
				"A plugin package cannot declare 'grants'; declare them in a softcode package it depends on."));
			return [];
		}

		if (format.Major == GrantsFormat.Major && format.Minor < GrantsFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning("grants",
				$"'grants' needs format {GrantsFormat} or later and is ignored; set format: \"{GrantsFormat}\"."));
			return [];
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("grants", "'grants' must map each role to a list of the package's permissions."));
			return [];
		}

		var result = new List<PackageGrantSpec>();
		foreach (var (rawRole, rawPermissions) in Normalize(map))
		{
			var path = $"grants.{rawRole}";
			var role = rawRole.Trim();
			if (!RoleNames.IsValidShortName(role))
			{
				issues.Add(PackageManifestIssue.Error(path, RoleNames.ShortNameRule));
				continue;
			}

			if (declared.Roles.Any(r => r.Slug == role))
			{
				issues.Add(PackageManifestIssue.Error(path, $"The package declares {role}; set its permissions under roles instead."));
				continue;
			}

			if (rawPermissions is not List<object> permissions)
			{
				issues.Add(PackageManifestIssue.Error(path, "A role's grants are a list of permissions."));
				continue;
			}

			foreach (var permission in permissions.Select(p => (p as string)?.Trim().ToLowerInvariant() ?? ""))
			{
				if (declared.Permissions.All(p => p.Name != permission))
				{
					issues.Add(PackageManifestIssue.Error(path,
						$"'{permission}' is not declared under permissions; a package grants only its own permissions."));
					continue;
				}

				if (result.Contains(new PackageGrantSpec(role, permission)))
				{
					issues.Add(PackageManifestIssue.Warning(path, $"{permission} is listed twice."));
					continue;
				}

				result.Add(new PackageGrantSpec(role, permission));
			}
		}

		return result;
	}
}
