using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MarkupString;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The MUSH-specific text operations: list splitting with PennMUSH's space semantics, space
/// compression, and glob/regexp matching that carries the markup of the input through into the
/// captured groups.
/// <para>
/// These are policy, not text mechanics, which is why they sit in the engine rather than in
/// <c>MarkupString</c>: the space-delimiter rule and the single-line wildcard mode are PennMUSH
/// behaviours, and nothing about <see cref="MarkupText"/> implies them.
/// </para>
/// </summary>
public static class MushText
{
	/// <summary>The generic MUSH error return, <c>#-1</c>.</summary>
	public static readonly MarkupText Error = MarkupText.Plain("#-1");

	/// <summary>The boolean-false / zero literal every predicate function returns.</summary>
	public static readonly MarkupText Zero = MarkupText.Plain("0");

	/// <summary>The boolean-true / one literal every predicate function returns.</summary>
	public static readonly MarkupText One = MarkupText.Plain("1");

	/// <summary>The default output delimiter.</summary>
	public static readonly MarkupText Comma = MarkupText.Plain(",");

	/// <summary>
	/// Splits <paramref name="text"/> on <paramref name="delimiter"/>. A single-space delimiter is
	/// PennMUSH's "words" delimiter and drops empty items, so runs of spaces do not produce blank
	/// list entries; every other delimiter keeps them.
	/// </summary>
	public static MarkupText[] SplitList(MarkupText delimiter, MarkupText text)
	{
		ArgumentNullException.ThrowIfNull(delimiter);
		ArgumentNullException.ThrowIfNull(text);

		var items = text.Split(delimiter.Text);
		return delimiter.Text == " " ? Array.FindAll(items, x => x.Length > 0) : items;
	}

	/// <summary>
	/// Collapses every run of two or more spaces down to a single space (PennMUSH's
	/// <c>PE_COMPRESS_SPACES</c>), leaving the markup on the surrounding text intact.
	/// </summary>
	/// <remarks>
	/// One left-to-right scan collecting the runs, then one <see cref="MarkupText.Splice"/>. The
	/// fast path returns the input untouched when there is no doubled space at all, which is the
	/// common case for parsed softcode.
	/// </remarks>
	public static MarkupText CompressSpaces(MarkupText text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (text.IndexOf("  ") < 0) return text;

		var raw = text.Text;
		var edits = new List<Edit>();
		var i = 0;
		while (i < raw.Length)
		{
			if (raw[i] != ' ')
			{
				i++;
				continue;
			}

			var start = i;
			while (i < raw.Length && raw[i] == ' ') i++;
			if (i - start > 1) edits.Add(new Edit(start, i - start, MarkupText.Space));
		}

		return edits.Count == 0 ? text : text.Splice(CollectionsMarshal.AsSpan(edits));
	}

	/// <summary>
	/// Compiles MUSH glob patterns (<c>*</c>, <c>?</c>, with <c>\</c> escaping either) to .NET regex.
	/// </summary>
	public static class Glob
	{
		/// <summary>
		/// Inline single-line mode, so the <c>.</c> that <c>*</c> and <c>?</c> compile to also matches
		/// a newline.
		/// </summary>
		/// <remarks>
		/// PennMUSH's matcher (src/wild.c) walks characters and has no notion of a line, so a wildcard
		/// spans a newline like any other character; .NET excludes <c>\n</c> from <c>.</c> unless told
		/// otherwise, which quietly made every SharpMUSH wildcard line-bound. It rides in the pattern
		/// STRING rather than as a <see cref="RegexOptions"/> flag because this is handed around as
		/// text and compiled by callers that pass their own options — <c>CommandAttributeScanner</c>
		/// compiles the <c>$</c>-command patterns itself, and that is the path where it showed: a
		/// multi-line argument matched no <c>$</c>-command at all and the player got a bare "Huh?".
		/// </remarks>
		private const string SingleLineMode = "(?s)";

		/// <summary>The characters <see cref="Regex.Escape"/> puts a backslash in front of.</summary>
		private static readonly SearchValues<char> Metacharacters = SearchValues.Create("\t\n\f\r #$()*+.?[\\^{|");

		/// <summary>The anchored regex equivalent to the glob <paramref name="pattern"/>.</summary>
		/// <remarks>
		/// The pattern is cut at every metacharacter; the text between them is literal and each
		/// metacharacter is spelled from its neighbours alone: a wildcard becomes a capture group unless
		/// a backslash precedes it, a backslash is an escape only when a wildcard follows it, and every
		/// other one is escaped the way <see cref="Regex.Escape"/> escapes it.
		/// </remarks>
		public static string ToRegex(string pattern)
		{
			ArgumentNullException.ThrowIfNull(pattern);
			var pieces = new List<string> { SingleLineMode, "^" };
			foreach (var literal in pattern.AsSpan().SplitAny(Metacharacters))
			{
				pieces.Add(pattern[literal]);
				if (literal.End.Value < pattern.Length)
				{
					pieces.Add(Metacharacter(pattern, literal.End.Value));
				}
			}

			pieces.Add("$");
			return string.Concat(pieces);
		}

		/// <summary>The regex text for the metacharacter at <paramref name="at"/> in <paramref name="pattern"/>.</summary>
		private static string Metacharacter(string pattern, int at)
		{
			var escaped = at > 0 && pattern[at - 1] == '\\';
			var escaping = at + 1 < pattern.Length && pattern[at + 1] is '*' or '?';
			return pattern[at] switch
			{
				'*' => escaped ? "*" : "(.*?)",
				'?' => escaped ? "?" : "(.)",
				'\\' => escaping ? @"\" : @"\\",
				'\n' => @"\n",
				'\r' => @"\r",
				'\t' => @"\t",
				'\f' => @"\f",
				var c => $@"\{c}",
			};
		}
	}

	/// <summary>Whether <paramref name="input"/> matches the glob <paramref name="pattern"/>.</summary>
	public static bool IsWildcardMatch(MarkupText input, MarkupText pattern) =>
		IsWildcardMatch(input, pattern.ToPlainText());

	/// <summary>Whether <paramref name="input"/> matches the glob <paramref name="pattern"/>.</summary>
	public static bool IsWildcardMatch(MarkupText input, string pattern) =>
		Regex.IsMatch(input.ToPlainText(), Glob.ToRegex(pattern));

	/// <summary>
	/// Every match of the regex <paramref name="pattern"/> in <paramref name="input"/>, paired with
	/// its groups sliced out of the input so each keeps the markup it carried.
	/// </summary>
	public static IEnumerable<(Match Match, IEnumerable<MarkupText> Groups)> GetMatches(MarkupText input, string pattern)
	{
		foreach (Match match in Regex.Matches(input.ToPlainText(), pattern))
		{
			var groups = match.Groups.Values.Select(g => input.Substring(g.Index, g.Length));
			yield return (match, groups);
		}
	}

	/// <inheritdoc cref="GetMatches(MarkupText, string)"/>
	public static IEnumerable<(Match Match, IEnumerable<MarkupText> Groups)> GetRegexpMatches(
		MarkupText input, MarkupText pattern) => GetMatches(input, pattern.ToPlainText());

	/// <summary>As <see cref="GetMatches(MarkupText, string)"/>, for a glob pattern.</summary>
	public static IEnumerable<(Match Match, IEnumerable<MarkupText> Groups)> GetWildcardMatches(
		MarkupText input, MarkupText pattern) => GetMatches(input, Glob.ToRegex(pattern.ToPlainText()));
}
