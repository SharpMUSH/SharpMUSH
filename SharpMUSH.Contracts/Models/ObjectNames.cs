using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// The characters a name may use: what PennMUSH allows in an object or player name. Role display
/// names and category names use the same rule.
/// </summary>
public static partial class ObjectNames
{
	/// <summary>Names that always resolve to something else, so nothing may be called by them.</summary>
	private static readonly HashSet<string> MagicCookies = new(["me", "here", "!", "home"], StringComparer.Ordinal);

	/// <summary>
	/// A legal object name: at least one character, no leading or trailing space, no control
	/// characters anywhere, and none of <c>[ ] % \ = &amp; |</c>. Interior spaces are fine
	/// ("a red ball"), and so is <c>;</c> — <c>@open</c> splits exit aliases on it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Anchored with <c>\A</c>/<c>\z</c> rather than <c>^</c>/<c>$</c>: in .NET <c>$</c> also
	/// matches immediately before a trailing newline, so <c>$</c> accepts <c>"name\n"</c>.
	/// </para>
	/// <para>
	/// The previous pattern had <c>$</c> but no start anchor at all, and its middle term matched the
	/// forbidden set instead of its complement, so <see cref="Regex.IsMatch(string)"/> could satisfy the
	/// whole expression against the last character or two of any input — every forbidden character
	/// passed as long as it was not at the very end.
	/// </para>
	/// </remarks>
	[GeneratedRegex(@"\A[^ \p{C}\[\]%\\=&\|](?:[^\p{C}\[\]%\\=&\|]*[^ \p{C}\[\]%\\=&\|])?\z")]
	private static partial Regex NamePattern();

	/// <summary>Whether <paramref name="name"/> is a legal name: the pattern above, ASCII only, and not a magic cookie.</summary>
	public static bool IsLegal(string name)
		=> NamePattern().IsMatch(name)
			&& !MagicCookies.Contains(name)
			&& name.EnumerateRunes().All(rune => rune.IsAscii);
}
