using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// Builds the regular expressions whose pattern text came from softcode.
/// </summary>
/// <remarks>
/// <para>
/// Two reasons this is not <c>new Regex(...)</c> at the call site.
/// </para>
/// <para>
/// <b>A pattern must not be able to stop the game.</b> The command queue runs one entry at a time
/// (<c>UseDefaultThreadPool(tp =&gt; tp.MaxConcurrency = 1)</c>, so the queue keeps PennMUSH's FIFO
/// order), and .NET's backtracking engine has no bound of its own. A pattern like <c>(a+)+$</c> against
/// input that cannot match runs for exponential time, so anyone who can set an attribute could stop
/// every player's commands, not just their own. <see cref="MatchTimeout"/> turns that into a
/// <see cref="RegexMatchTimeoutException"/> for the one command that asked for it.
/// </para>
/// <para>
/// <b>Construction is the expensive part.</b> A LISTEN pattern or a wildcard <c>lattr</c> was compiled
/// fresh for every message; the same text now hands back the same instance. Bounded, because the
/// pattern text is player-supplied and an unbounded map keyed by it is a memory leak with a name.
/// </para>
/// </remarks>
public static class SoftcodeRegex
{
	/// <summary>
	/// How long one match may run. Long enough that no honest pattern over a line of MUSH text comes
	/// near it, short enough that hitting it is not itself the outage.
	/// </summary>
	public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

	/// <summary>
	/// How many compiled patterns to keep. The pattern text is player-supplied, so an unbounded map
	/// keyed by it is a memory leak with a name.
	/// </summary>
	public const int Capacity = 1024;

	/// <summary>
	/// A least-recently-used cache rather than a dictionary with a bound, which is what this was first.
	/// A dictionary that stops admitting once it is full never admits again: the first thousand
	/// patterns a server happened to see would hold the cache for its whole life, and the $-command
	/// every player types a hundred times a day could be locked out by a thousand one-off
	/// <c>lattr</c> globs from the morning. Compaction evicts the coldest tenth instead, so a hot
	/// pattern can always get back in.
	/// </summary>
	private static readonly MemoryCache Cache = new(new MemoryCacheOptions
	{
		SizeLimit = Capacity,
		CompactionPercentage = 0.1,
	});

	private static readonly MemoryCacheEntryOptions EntryOptions = new() { Size = 1 };

	/// <summary>How many are held. For the test that the bound holds under concurrent misses.</summary>
	public static int CachedCount => Cache.Count;

	/// <summary>
	/// A regex for <paramref name="pattern"/>, time-bounded and shared with other callers asking for
	/// the same text and options.
	/// </summary>
	/// <exception cref="ArgumentException">The pattern is not a valid regular expression.</exception>
	public static Regex Create(string pattern, RegexOptions options)
	{
		var key = (pattern, options);
		if (Cache.TryGetValue(key, out Regex? cached) && cached is not null)
		{
			return cached;
		}

		// Built before it is stored so an invalid pattern throws to the caller, and so the constructor
		// never runs while the cache holds a lock. A concurrent miss on the same key builds a second
		// instance and one of them wins; they are equivalent, and the loser is collected.
		var regex = new Regex(pattern, options, MatchTimeout);
		Cache.Set(key, regex, EntryOptions);
		return regex;
	}

	/// <summary>
	/// <paramref name="regex"/> against <paramref name="input"/>, where a pattern that cannot finish in
	/// <see cref="MatchTimeout"/> does not match.
	/// </summary>
	/// <remarks>
	/// For the engine's own matching — command discovery, listen patterns, sitelock, help search — where
	/// there is no player waiting on a result to be told anything. Bounding the match turned a hang into
	/// a <see cref="RegexMatchTimeoutException"/>, and an exception nobody catches on those paths is a
	/// different outage, not a fix: it would abort command matching rather than let the next pattern be
	/// tried. Softcode functions do NOT use this — they have an answer to give, and say
	/// <c>#-1 REGEXP TIMEOUT</c>.
	/// </remarks>
	public static bool IsMatch(Regex regex, string input)
	{
		try
		{
			return regex.IsMatch(input);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}

	/// <summary><see cref="IsMatch"/>, for the callers that need the groups. Null when it timed out.</summary>
	public static Match? Match(Regex regex, string input)
	{
		try
		{
			return regex.Match(input);
		}
		catch (RegexMatchTimeoutException)
		{
			return null;
		}
	}

	/// <summary>
	/// A regex for a MUSH wildcard pattern, matching what PennMUSH's <c>wild_match_test</c>
	/// (<c>src/wild.c</c>) does with the same pattern: whole-string, case-insensitive, one capture
	/// register per <c>*</c> or <c>?</c>, and <c>\</c> making the next character literal.
	/// </summary>
	/// <remarks>
	/// A single glob is linear no matter what it is matched against. Two or more can backtrack
	/// polynomially — a subject the pattern cannot match has to be re-split every way before the engine
	/// gives up, which measured at 2.3 seconds for six stars over sixty characters — so those, and only
	/// those, get the non-backtracking engine and its linear guarantee. It costs about half again as
	/// much per match, which is worth paying to remove seconds and not worth paying on the <c>tes*</c>
	/// that nearly every real pattern is. <c>Compiled</c> is dropped when it applies, the two being
	/// mutually exclusive.
	/// </remarks>
	/// <param name="caseSensitive">
	/// PennMUSH takes this as an argument: <c>quick_wild</c> passes 0, so a $-command, an @listen and
	/// grab() are all case-insensitive, and that is the default here. <c>grep_util</c> passes 1 unless
	/// the caller used the "i" variant, so wildgrep asks for true.
	/// </param>
	public static Regex Wildcard(string wildcardPattern, RegexOptions options = RegexOptions.None,
		bool caseSensitive = false)
	{
		var pattern = MushText.Glob.ToRegex(wildcardPattern);

		if (!caseSensitive)
		{
			options |= RegexOptions.IgnoreCase;
		}

		if (GlobGroups(pattern) >= 2)
		{
			options = (options & ~RegexOptions.Compiled) | RegexOptions.NonBacktracking;
		}

		return Create(pattern, options);
	}

	/// <summary>How many <c>*</c> the pattern turned into, counted on the translated form.</summary>
	private static int GlobGroups(string pattern) => pattern.AsSpan().Count(GlobGroup);

	private const string GlobGroup = "(.*?)";
}
