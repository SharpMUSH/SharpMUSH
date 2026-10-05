using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Authorization;

/// <summary>The rules for naming a <see cref="Models.CustomPermission"/>.</summary>
public static partial class CustomPermissions
{
	/// <summary>The longest name allowed.</summary>
	public const int MaxNameLength = 64;

	/// <summary>
	/// Namespaces the server owns: <c>game.</c> holds the flags and powers (and <c>@power/add</c> may add
	/// more), and <c>control.</c> and <c>protect.</c> decide who controls whom.
	/// </summary>
	public static readonly IReadOnlyList<string> ReservedPrefixes = ["game.", "control.", "protect."];

	[GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$")]
	private static partial Regex NamePattern();

	/// <summary>
	/// Whether <paramref name="scope"/> may name a custom permission: two or more dot-separated parts of
	/// lowercase letters, digits and <c>_</c>, starting with a letter; not a built-in permission and not
	/// in a reserved namespace.
	/// </summary>
	public static bool IsValidName(string scope)
		=> scope.Length <= MaxNameLength
			&& NamePattern().IsMatch(scope)
			&& !PortalPermission.IsKnown(scope)
			&& !ReservedPrefixes.Any(prefix => scope.StartsWith(prefix, StringComparison.Ordinal));
}
