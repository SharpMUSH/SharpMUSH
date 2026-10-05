namespace SharpMUSH.Library.Models;

/// <summary>
/// A named group in one of the two category lists: roles (<c>System</c>, <c>Staff</c>) or custom
/// permissions. The portal's Roles page lists each by its category. Stored as system data with the roles.
/// </summary>
/// <param name="Name">The name, as it was given (see <see cref="Authorization.Categories.IsValidName"/>); compared without case.</param>
/// <param name="Description">What belongs in it.</param>
/// <param name="CreatedAt">Creation time (unix ms).</param>
public sealed record RoleCategory(string Name, string Description, long CreatedAt);

/// <summary>Which category list a <see cref="RoleCategory"/> belongs to. The two lists are separate: a role
/// goes in a role category and a custom permission in a permission category, and a name may be in both.</summary>
public enum CategoryKind
{
	/// <summary>Categories of roles.</summary>
	Role,

	/// <summary>Categories of custom permissions.</summary>
	Permission
}
