using System.Text;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Why a rule's pattern cannot be used.</summary>
public enum HighlightProblem
{
	/// <summary>Nothing to look for.</summary>
	Empty,

	/// <summary>Longer than <see cref="HighlightRule.MaxPatternLength"/>.</summary>
	TooLong,

	/// <summary>Read as a regular expression, it does not compile.</summary>
	NotARegex
}

/// <summary>What the rules a line matched do to it: the first colour, the first sound, and any notification.</summary>
/// <param name="Rule">The first rule that matched, which names the notification.</param>
public sealed record HighlightHit(HighlightRule Rule, HighlightColour Colour, HighlightSound Sound, bool Notify);

/// <summary>
/// A player's highlight rules, compiled once, matched against the plain text of a line.
/// </summary>
/// <remarks>
/// A regex runs with a short timeout: a pattern that backtracks without end on some line is taken as not
/// matching that line rather than stalling the terminal. A rule whose pattern does not compile is left out
/// (<see cref="Validate"/> says why, for the editor).
/// </remarks>
public sealed class HighlightMatcher
{
	/// <summary>How long one rule may spend on one line.</summary>
	/// <remarks>Short, since the terminal runs on the page's one thread: a pattern that backtracks badly costs this on every line.</remarks>
	public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(10);

	/// <summary>Matches nothing.</summary>
	public static readonly HighlightMatcher Empty = new([]);

	private readonly (HighlightRule Rule, Regex Regex)[] _rules;

	public HighlightMatcher(IEnumerable<HighlightRule> rules) =>
		_rules = rules
			.Where(rule => rule is { Enabled: true, HasEffect: true })
			.SelectMany(rule => Compile(rule) is Regex regex ? [(rule, regex)] : Array.Empty<(HighlightRule, Regex)>())
			.ToArray();

	/// <summary>True when no rule can match anything.</summary>
	public bool IsEmpty => _rules.Length == 0;

	/// <summary>What the rules do to a server line, or nothing when none matched.</summary>
	public Found<HighlightHit> Match(string text, TerminalLineComm comm)
	{
		var plain = text.TrimEnd('\r', '\n');
		HighlightRule? first = null;
		var colour = HighlightColour.None;
		var sound = HighlightSound.None;
		var notify = false;
		foreach (var (rule, regex) in _rules)
		{
			if (!Applies(rule.Lines, comm) || !IsMatch(regex, plain)) continue;
			first ??= rule;
			if (colour == HighlightColour.None) colour = rule.Colour;
			if (sound == HighlightSound.None) sound = rule.Sound;
			notify |= rule.Notify;
		}
		return first is null ? new NotFound() : new HighlightHit(first, colour, sound, notify);
	}

	/// <summary>Why the rule's pattern cannot be used, or nothing when it can.</summary>
	public static Found<HighlightProblem> Validate(HighlightRule rule)
	{
		if (string.IsNullOrWhiteSpace(rule.Pattern)) return HighlightProblem.Empty;
		if (rule.Pattern.Length > HighlightRule.MaxPatternLength) return HighlightProblem.TooLong;
		return Compile(rule) is Regex ? new NotFound() : HighlightProblem.NotARegex;
	}

	private static bool Applies(HighlightLines lines, TerminalLineComm comm) => (lines, comm) switch
	{
		(HighlightLines.All, _) => true,
		(HighlightLines.Pages, TerminalLineComm.Page) => true,
		(HighlightLines.Channels, TerminalLineComm.Channel) => true,
		(HighlightLines.Other, TerminalLineComm.None) => true,
		_ => false
	};

	private static bool IsMatch(Regex regex, string text)
	{
		try
		{
			return regex.IsMatch(text);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}

	private static Result<Regex> Compile(HighlightRule rule)
	{
		if (string.IsNullOrWhiteSpace(rule.Pattern) || rule.Pattern.Length > HighlightRule.MaxPatternLength)
			return new Error<string>("No pattern.");
		var options = RegexOptions.CultureInvariant | (rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
		var source = rule.Match switch
		{
			HighlightMatch.Words => WordsPattern(rule.Pattern),
			HighlightMatch.Glob => GlobPattern(rule.Pattern),
			_ => rule.Pattern
		};
		try
		{
			return new Regex(source, options, MatchTimeout);
		}
		catch (ArgumentException ex)
		{
			return new Error<string>(ex.Message);
		}
	}

	/// <summary>
	/// The words as one phrase, standing alone: not inside a longer word on either side. Runs of spaces in the
	/// pattern match any run of white space, since the game may wrap or pad.
	/// </summary>
	private static string WordsPattern(string words)
	{
		var parts = words.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
		return $@"(?<![\p{{L}}\p{{N}}_]){string.Join(@"\s+", parts)}(?![\p{{L}}\p{{N}}_])";
	}

	/// <summary>A wildcard pattern over the whole line, as MUSH globs read: <c>*</c> and <c>?</c>, and <c>\</c> escapes.</summary>
	private static string GlobPattern(string glob)
	{
		var pattern = new StringBuilder("^");
		for (var at = 0; at < glob.Length; at++)
		{
			var c = glob[at];
			pattern.Append(c switch
			{
				'*' => "(?s:.*)",
				'?' => "(?s:.)",
				'\\' when at + 1 < glob.Length => Regex.Escape(glob[++at].ToString()),
				_ => Regex.Escape(c.ToString())
			});
		}
		return pattern.Append('$').ToString();
	}
}
