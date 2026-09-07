using System.Collections.Concurrent;
using System.Text.RegularExpressions;

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
	/// How many compiled patterns to keep. Past this the cache stops admitting new ones rather than
	/// growing: the excess is recompiled per use, which is the old behaviour and still bounded work.
	/// </summary>
	public const int Capacity = 1024;

	/// <summary>How many are held. For the test that the bound holds under concurrent misses.</summary>
	public static int CachedCount => Cache.Count;

	private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> Cache = new();

	/// <summary>
	/// Held only across the count-and-admit on a miss. Reading the count and then adding are two steps,
	/// and concurrent misses could each see room and each insert, putting the cache over its bound for
	/// the life of the process. Hits never reach it.
	/// </summary>
	private static readonly Lock Admission = new();

	/// <summary>
	/// A regex for <paramref name="pattern"/>, time-bounded and shared with other callers asking for
	/// the same text and options.
	/// </summary>
	/// <exception cref="ArgumentException">The pattern is not a valid regular expression.</exception>
	public static Regex Create(string pattern, RegexOptions options)
	{
		if (Cache.TryGetValue((pattern, options), out var cached))
		{
			return cached;
		}

		// Built outside the map so an invalid pattern throws to the caller rather than being retried
		// by every subsequent request, and so the constructor never runs under a dictionary lock.
		var regex = new Regex(pattern, options, MatchTimeout);

		lock (Admission)
		{
			return Cache.Count >= Capacity
				? regex
				: Cache.GetOrAdd((pattern, options), regex);
		}
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
}
