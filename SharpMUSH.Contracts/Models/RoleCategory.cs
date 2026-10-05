namespace SharpMUSH.Library.Models;

/// <summary>
/// A group of roles and custom permissions, such as <c>Staff</c> or <c>Scenes</c>. The portal's Roles
/// page lists both by category. Stored as system data with the roles.
/// </summary>
/// <param name="Name">The name, as it was given (see <see cref="Authorization.Categories.IsValidName"/>); compared without case.</param>
/// <param name="Description">What belongs in it.</param>
/// <param name="CreatedAt">Creation time (unix ms).</param>
public sealed record RoleCategory(string Name, string Description, long CreatedAt);
