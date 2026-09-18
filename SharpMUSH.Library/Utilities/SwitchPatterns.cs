using System.Text.RegularExpressions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// One case of <c>switch()</c>, <c>@switch</c> or <c>@select</c>: PennMUSH's <c>local_wild_match_case</c>
/// and, for <c>/regexp</c>, <c>regexp_match_case_r</c> (<c>src/wild.c</c>).
/// </summary>
public static class SwitchPatterns
{
	/// <summary>
	/// Whether <paramref name="subject"/> matches <paramref name="pattern"/>, leaving the match's captures
	/// in <paramref name="captures"/> for <c>$0</c>-<c>$9</c> to read (and nothing there when it does not
	/// match). A glob starting with <c>&gt;</c> or <c>&lt;</c> orders the subject against the rest and
	/// captures nothing; any other glob captures once per <c>*</c> or <c>?</c>, from 0. A regexp is
	/// case-insensitive and captures its groups, numbered as PCRE numbers them.
	/// </summary>
	/// <exception cref="ArgumentException">A regexp pattern that does not compile.</exception>
	/// <exception cref="RegexMatchTimeoutException">A regexp that cannot finish in time.</exception>
	public static bool Matches(MString subject, string pattern, bool regexp, RegexpCaptureFrame captures)
	{
		captures.Clear();
		var plainSubject = subject.ToPlainText();

		if (regexp)
		{
			var regex = SoftcodeRegex.Create(pattern, RegexOptions.IgnoreCase);
			var regexMatch = regex.Match(plainSubject);
			if (!regexMatch.Success) return false;
			captures.Fill(regex, regexMatch, subject);
			return true;
		}

		if (pattern is ['>' or '<', ..])
		{
			return IncomingFilterPatterns.Matches(pattern, plainSubject, regexp: false, caseSensitive: false);
		}

		var match = SoftcodeRegex.Wildcard(pattern).Match(plainSubject);
		if (!match.Success) return false;
		captures.FillWildcard(match, subject);
		return true;
	}
}
