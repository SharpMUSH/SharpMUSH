using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models;

/// <summary>
/// A permission a game defines for itself, such as <c>scene.close</c>, for its own softcode to check with
/// <c>permission()</c> or a <c>PERM^</c> lock. Roles and overrides allow or deny it like a built-in one.
/// Stored as system data with the roles.
/// </summary>
/// <param name="Scope">The name, lowercase (see <see cref="Authorization.CustomPermissions.IsValidName"/>).</param>
/// <param name="Category">What it is grouped under on the portal's Roles page (<see cref="Authorization.Categories"/>).</param>
/// <param name="Description">What holding it lets someone do, shown beside it in lists.</param>
/// <param name="CreatedAt">Creation time (unix ms).</param>
public sealed record CustomPermission(string Scope, string Category, string Description, long CreatedAt);

/// <summary>Every override of one permission: who has it allowed or denied directly, apart from roles.</summary>
/// <param name="Accounts">Account id → its setting.</param>
/// <param name="Objects">Object number → its setting.</param>
public sealed record PermissionOverrideHolders(
	IReadOnlyDictionary<string, PermissionState> Accounts,
	IReadOnlyDictionary<int, PermissionState> Objects)
{
	/// <summary>Account id → the role its override's reach names, for those that have one.</summary>
	public IReadOnlyDictionary<string, string> AccountReaches { get; init; } = new Dictionary<string, string>();

	/// <summary>Object number → the role its override's reach names, for those that have one.</summary>
	public IReadOnlyDictionary<int, string> ObjectReaches { get; init; } = new Dictionary<int, string>();
}
