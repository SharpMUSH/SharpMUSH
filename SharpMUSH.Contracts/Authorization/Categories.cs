namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The category every role and custom permission carries, which the portal's Roles page groups them
/// under. Free text chosen by the game, such as <c>Staff</c> or <c>Scenes</c>; compared without case.
/// </summary>
public static class Categories
{
	/// <summary>The longest category allowed.</summary>
	public const int MaxLength = 32;

	/// <summary>The category of the built-in system roles.</summary>
	public const string System = "System";

	/// <summary>The category of the starter staff roles.</summary>
	public const string Staff = "Staff";

	/// <summary>
	/// <paramref name="category"/> trimmed when it is a usable category: not blank, at most
	/// <see cref="MaxLength"/> characters, with no <c>/</c> (which <c>@role</c> uses to separate it from
	/// what follows) and no control characters; otherwise null.
	/// </summary>
	public static string? Normalize(string? category)
	{
		var trimmed = category?.Trim() ?? "";
		return trimmed.Length is > 0 and <= MaxLength && !trimmed.Contains('/') && !trimmed.Any(char.IsControl)
			? trimmed
			: null;
	}

	/// <summary>The refusal sentence for a category <see cref="Normalize"/> rejected.</summary>
	public static string Rule
		=> $"A category is required: 1 to {MaxLength} characters, without '/'.";
}
