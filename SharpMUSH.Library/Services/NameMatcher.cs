using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH <c>match.c</c>'s name rules, with nothing else in them: given a candidate and the name
/// a player typed, does it answer, exactly or by abbreviation.
/// </summary>
/// <remarks>
/// Pure and synchronous — no database, no permissions, no notification. That is the point of holding
/// it apart from <see cref="LocateService"/>, whose remaining work is the search scopes and the
/// gates applied to what these rules select.
/// </remarks>
internal static class NameMatcher
{
	internal enum MatchKind
	{
		None,
		Partial,
		Exact
	}

	/// <summary>
	/// How <paramref name="cur"/> answers to <paramref name="name"/>. Aliases are a player's and an
	/// exit's only (match.c's <c>match_aliases</c>), and a partial match is <c>string_match</c> —
	/// a prefix, and never against an exit.
	/// </summary>
	internal static MatchKind Classify(AnySharpObject cur, string name, bool allowPartial)
	{
		var objectName = cur.Object().Name;
		// match_aliases answers for players and exits and returns 0 for everything else, so the array is
		// not even worth fetching otherwise.
		ReadOnlySpan<string> aliases = cur.IsPlayer || cur.IsExit ? cur.Aliases : [];

		if (AnyAliasMatches(aliases, name)
				|| objectName.Equals(name, StringComparison.OrdinalIgnoreCase))
		{
			return MatchKind.Exact;
		}

		if (!allowPartial) return MatchKind.None;

		// Name only, never aliases: MATCH_LIST's partial branch is string_match(Name(match), name) and
		// there is no partial alias match anywhere in match.c. A player's aliases answer in the exact
		// branch above and nowhere else. Abbreviating a player is still served here, by string_match
		// over the player's name. See issue #794.
		return !cur.IsExit && StringMatch(objectName, name)
			? MatchKind.Partial
			: MatchKind.None;
	}

	/// <summary>
	/// PennMUSH's <c>string_match</c> (strutil.c): <paramref name="sub"/> is a prefix of <em>any word</em>
	/// of <paramref name="src"/>, not just of the whole string. `sword` matches `Big Sword`, which is how
	/// players ordinarily refer to things — most object names are more than one word, and testing only
	/// the first left every one of them reachable by its leading word or in full and no other way.
	/// </summary>
	/// <remarks>
	/// The word separator is <c>isalnum</c>, not whitespace: `Myrddin's` advances past the apostrophe to
	/// `s`, so splitting on spaces is not the same function. Runs once per candidate per locate, so it
	/// walks spans rather than allocating.
	/// </remarks>
	internal static bool StringMatch(ReadOnlySpan<char> src, ReadOnlySpan<char> sub)
	{
		if (sub.IsEmpty) return false;

		while (!src.IsEmpty)
		{
			if (src.StartsWith(sub, StringComparison.OrdinalIgnoreCase)) return true;

			// Scan to the beginning of the next word, exactly as string_match does. IsLetterOrDigit stands
			// in for isalnum, which PennMUSH runs under a UTF-8 ctype locale, so both are Unicode-aware.
			var i = 0;
			while (i < src.Length && char.IsLetterOrDigit(src[i])) i++;
			while (i < src.Length && !char.IsLetterOrDigit(src[i])) i++;

			// One of those two scans always advances, so i >= 1 — but this is a per-candidate loop and a
			// hang here would be a denial of service, so don't rest the whole thing on that reasoning.
			src = src[Math.Max(i, 1)..];
		}

		return false;
	}

	/// <summary>
	/// match.c's <c>match_aliases</c>, whose <c>check_alias</c> compares each <c>;</c>-separated entry
	/// for equality.
	/// </summary>
	/// <remarks>
	/// A span walk rather than <c>Any(a =&gt; …)</c> or <c>Contains(name, comparer)</c>: this runs once
	/// per candidate per locate, and the predicate overload captures <paramref name="name"/> into a fresh
	/// closure each time while the comparer overload still boxes the array's enumerator.
	/// </remarks>
	private static bool AnyAliasMatches(ReadOnlySpan<string> aliases, string name)
	{
		foreach (var alias in aliases)
		{
			if (alias.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

}
