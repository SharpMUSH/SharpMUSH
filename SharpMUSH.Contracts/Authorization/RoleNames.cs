using System.Text.RegularExpressions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>The rules for naming a <see cref="SharpRole"/>.</summary>
public static partial class RoleNames
{
	/// <summary>The longest short name or display name allowed.</summary>
	public const int MaxLength = 32;

	[GeneratedRegex("^[a-z0-9_-]{1,32}$")]
	private static partial Regex ShortNamePattern();

	/// <summary>The sentence a refusal gives for a short name <see cref="IsValidShortName"/> rejects.</summary>
	public static readonly string ShortNameRule = $"A role name is 1 to {MaxLength} lowercase letters, digits, '-' or '_'.";

	/// <summary>The sentence a refusal gives for a display name <see cref="IsValidDisplayName"/> rejects.</summary>
	public static readonly string DisplayNameRule = $"A role's display name is 1 to {MaxLength} characters a player name may use.";

	/// <summary>
	/// Whether <paramref name="name"/> may be a role's short name (<see cref="SharpRole.Slug"/>): what
	/// <c>roles()</c> lists and <c>hasrole()</c> takes, so no spaces.
	/// </summary>
	public static bool IsValidShortName(string name) => ShortNamePattern().IsMatch(name);

	/// <summary>Whether <paramref name="name"/> may be a role's display name: the characters a player name may use, spaces included.</summary>
	public static bool IsValidDisplayName(string name) => name.Length <= MaxLength && ObjectNames.IsLegal(name);
}
