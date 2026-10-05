namespace SharpMUSH.Library.Models;

/// <summary>
/// A permission a game defines for itself, such as <c>scene.close</c>, for its own softcode to check with
/// <c>permission()</c> or a <c>PERM^</c> lock. Roles and overrides allow or deny it like a built-in one.
/// Stored as system data with the roles.
/// </summary>
/// <param name="Scope">The name, lowercase (see <see cref="Authorization.CustomPermissions.IsValidName"/>).</param>
/// <param name="Description">What holding it lets someone do, shown beside it in lists.</param>
/// <param name="CreatedAt">Creation time (unix ms).</param>
public sealed record CustomPermission(string Scope, string Description, long CreatedAt);
