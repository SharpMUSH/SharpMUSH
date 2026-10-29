using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// The objects a lock key names by a bare <c>#N</c>, found the way PennMUSH's <c>parse_boolexp</c> reads a key:
/// operators and grouping between leaves, each leaf running to the next unescaped <c>&amp;</c>, <c>|</c> or
/// <c>)</c>. Only a leaf that is a <c>#N</c> alone, or after the <c>=</c>, <c>+</c>, <c>@</c> or <c>$</c> of an
/// is, carry, indirect or owner lock (and for an indirect lock the lock it names, <c>@#N/Basic</c>), names an
/// object; anything else, such as the value of <c>TAG:foo #2</c>, is a literal.
/// </summary>
public static partial class LockKeyReferences
{
	/// <summary>
	/// <paramref name="key"/> with the <c>#N</c> of each object leaf replaced by what <paramref name="replace"/>
	/// returns for <c>N</c>, or kept when it returns <see langword="null"/>. A leaf that already carries a
	/// creation time (<c>#N:ms</c>) is not a bare <c>#N</c> and is kept as written.
	/// </summary>
	public static string Rewrite(string key, Func<int, string?> replace)
		=> RewriteLeaves(key, leaf => RewriteLeaf(leaf, replace));

	/// <summary>
	/// <paramref name="key"/> with the creation time taken off each object leaf (<c>#N:ms</c> written as
	/// <c>#N</c>), so a key the store stamped compares equal to the text it was set from.
	/// </summary>
	public static string WithoutCreationTimes(string key)
		=> key.Contains(':') ? RewriteLeaves(key, Unstamped) : key;

	private static string RewriteLeaves(string key, Func<string, string> rewriteLeaf)
	{
		var rewritten = new StringBuilder(key.Length);
		var at = 0;
		while (at < key.Length)
		{
			// Between leaves: the operators and grouping parse_boolexp consumes before it reads an operand.
			if (key[at] is '&' or '|' or '(' or ')' or '!' || char.IsWhiteSpace(key[at]))
			{
				rewritten.Append(key[at++]);
				continue;
			}

			// A leaf runs to the next unescaped &, | or ), as parse_boolexp_L reads it.
			var end = at;
			var escaped = false;
			while (end < key.Length && (escaped || key[end] is not ('&' or '|' or ')')))
			{
				escaped = !escaped && key[end] == '\\';
				end++;
			}

			rewritten.Append(rewriteLeaf(key[at..end]));
			at = end;
		}

		return rewritten.ToString();
	}

	private static string RewriteLeaf(string leaf, Func<int, string?> replace)
	{
		var match = ObjectLeaf().Match(leaf);
		if (!match.Success
			|| match.Groups["created"].Success
			|| (match.Groups["lock"].Success && match.Groups["token"].Value != "@")
			|| !int.TryParse(match.Groups["number"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
			|| replace(number) is not { } replacement)
		{
			return leaf;
		}

		var digits = match.Groups["number"];
		return $"{leaf[..(digits.Index - 1)]}{replacement}{leaf[(digits.Index + digits.Length)..]}";
	}

	private static string Unstamped(string leaf)
	{
		var match = ObjectLeaf().Match(leaf);
		if (!match.Success
			|| !match.Groups["created"].Success
			|| (match.Groups["lock"].Success && match.Groups["token"].Value != "@"))
		{
			return leaf;
		}

		var created = match.Groups["created"];
		return $"{leaf[..created.Index]}{leaf[(created.Index + created.Length)..]}";
	}

	[GeneratedRegex(@"^(?<token>[=+@$]?)\s*#(?<number>\d+)(?<created>:\d+)?(?<lock>/\S+)?\s*$")]
	private static partial Regex ObjectLeaf();
}
