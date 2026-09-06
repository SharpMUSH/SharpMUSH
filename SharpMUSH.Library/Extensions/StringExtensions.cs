using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Extensions;

public static class StringExtensions
{
	private const string Asterisk = "\\*";
	private const string Question = "\\?";

	/// <summary>
	/// Converts a glob pattern to a regex pattern. Unanchored and without the single-line mode
	/// <see cref="SharpMUSH.Library.Markup.MushText.Glob"/> applies — the two are not interchangeable.
	/// </summary>
	/// <param name="str">Glob Pattern</param>
	/// <returns>Regex Pattern</returns>
	public static string GlobToRegex(this string str)
		=> Regex.Escape(str)
			.Replace(Asterisk, ".*?")
			.Replace(Question, ".");
}