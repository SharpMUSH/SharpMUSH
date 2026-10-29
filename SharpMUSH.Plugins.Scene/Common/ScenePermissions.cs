using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Common;

/// <summary>
/// The scene package's permissions that the plugin reads itself. The package declares them (and the roles that hold
/// them by default); a game moves them with the role tools. A permission no installed package declares restricts
/// nothing, so a game running the plugin without the package reads and records as it did before the package
/// existed.
/// </summary>
public static class ScenePermissions
{
	/// <summary>Read scenes the reader is not in.</summary>
	public const string View = "scene.view";

	/// <summary>Have poses recorded into scenes.</summary>
	public const string Pose = "scene.pose";

	/// <summary>Whether <paramref name="grants"/> hold <paramref name="scope"/>, or no package declares it.</summary>
	public static bool Allows(ObjectGrants grants, string scope)
		=> grants.Canonical(scope) is not { } known || grants.Has(known);

	/// <inheritdoc cref="Allows(ObjectGrants, string)"/>
	public static async ValueTask<bool> AllowsAsync(AnySharpObject who, string scope, CancellationToken cancellationToken = default)
		=> Allows(await who.GrantsAsync(cancellationToken), scope);

	/// <summary>
	/// Whether the <c>everyone</c> role allows <paramref name="scope"/>: what a reader with no character holds, or
	/// true when no package declares it.
	/// </summary>
	public static async ValueTask<bool> EveryoneAllowsAsync(IRoleRegistryService roles, IPermissionResolver resolver,
		string scope, CancellationToken cancellationToken = default)
	{
		var custom = (await roles.GetCustomPermissionsAsync(cancellationToken))
			.Select(permission => permission.Scope)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (!custom.Contains(scope)) return true;

		var everyone = (await roles.GetRolesAsync(cancellationToken)).Where(BuiltInRoles.IsEveryone).ToArray();
		var context = new PermissionContext(everyone, new Dictionary<string, PermissionState>(), false) { CustomScopes = custom };
		return resolver.Resolve(context).Contains(scope, StringComparer.OrdinalIgnoreCase);
	}
}
