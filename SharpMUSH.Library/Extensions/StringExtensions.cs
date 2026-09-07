namespace SharpMUSH.Library.Extensions;

public static class StringExtensions
{
	/// <summary>
	/// Converts a MUSH wildcard pattern to a regex pattern.
	/// </summary>
	/// <remarks>
	/// It delegates, because it used to be a second transformation that disagreed with the first:
	/// <c>Regex.Escape(str).Replace("\\*", ".*?")</c>, with no anchors and no case folding. That made
	/// <c>grab(list, ab)</c> match an element <c>xxabxx</c>, where PennMUSH's <c>wild_match_test</c>
	/// requires the whole element, and made it case-sensitive where PennMUSH is not. Two spellings of
	/// one idea is how they came to disagree, so now there is one.
	/// </remarks>
	/// <param name="str">Wildcard pattern</param>
	/// <returns>Regex pattern</returns>
	public static string GlobToRegex(this string str) => MModule.getWildcardMatchAsRegex2(str);
}
