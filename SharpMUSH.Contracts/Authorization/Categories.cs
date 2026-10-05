using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The rules for a <see cref="RoleCategory"/>. Every role and custom permission sits in one, and the
/// portal's Roles page groups them by it. A category must exist before anything is put in it.
/// </summary>
public static class Categories
{
	/// <summary>The longest category name allowed.</summary>
	public const int MaxNameLength = 32;

	/// <summary>The longest category description allowed.</summary>
	public const int MaxDescriptionLength = 200;

	/// <summary>The category of the built-in system roles.</summary>
	public const string System = "System";

	/// <summary>The category of the starter staff roles.</summary>
	public const string Staff = "Staff";

	/// <summary>The categories a new world starts with.</summary>
	public static readonly IReadOnlyList<RoleCategory> Seeds =
	[
		new(System, "Roles the server defines: everyone, player, guest, builder, royalty, wizard and god.", 0),
		new(Staff, "Roles for the people who help run the game.", 0)
	];

	/// <summary>The sentence a refusal gives for a name <see cref="IsValidName"/> rejects.</summary>
	public static readonly string NameRule =
		$"A category name is 1 to {MaxNameLength} characters a player name may use, without '/'.";

	/// <summary>
	/// Whether <paramref name="name"/> may name a category: the characters a player name may use
	/// (<see cref="ObjectNames.IsLegal"/>), spaces included, at most <see cref="MaxNameLength"/> long,
	/// and no <c>/</c>, which <c>@role</c> uses to separate a category from what follows it.
	/// </summary>
	public static bool IsValidName(string name)
		=> name.Length <= MaxNameLength && !name.Contains('/') && ObjectNames.IsLegal(name);
}
