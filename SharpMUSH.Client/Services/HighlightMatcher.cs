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
	NotARegex,

	/// <summary>The rule targets a group the pattern does not have.</summary>
	NoSuchGroup,

	/// <summary>The rewrite is longer than <see cref="HighlightRule.MaxRewriteLength"/>.</summary>
	RewriteTooLong
}

/// <summary>A part of a line one rule targets: where it is in the line's plain text, its look, and its rewrite.</summary>
/// <param name="Start">Where the part starts in the line's plain text.</param>
/// <param name="Length">How long it is; 0 for a rewrite that inserts text.</param>
/// <param name="Rewrite">The text shown in its place, or none.</param>
public sealed record HighlightMark(int Start, int Length, HighlightLook Look, string? Rewrite)
{
	public int End => Start + Length;
}

/// <summary>
/// What the rules a line matched do to it. The whole line takes the first rule's look and rewrite that targets the
/// whole line; a part of it takes the first rule's that targets that part, and a later rule's part that overlaps
/// it is left out. The first sound plays; any rule can notify or hide the line.
/// </summary>
/// <param name="Rule">The first rule that matched, which names the notification.</param>
/// <param name="Line">The whole line's look.</param>
/// <param name="Marks">The parts of the line rules target, in order along the line.</param>
/// <param name="Rewrite">The text shown in place of the whole line, or none.</param>
/// <param name="Hide">The line is not shown.</param>
public sealed record HighlightHit(HighlightRule Rule, HighlightLook Line, IReadOnlyList<HighlightMark> Marks, HighlightSound Sound,
	bool Notify, bool Hide = false, string? Rewrite = null)
{
	/// <summary>The line is shown as it was sent, apart from its look.</summary>
	public bool KeepsText => Marks.Count == 0 && Rewrite is null;
}

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

	/// <summary>The most places one rule marks in one line.</summary>
	public const int MaxMarksPerRule = 32;

	/// <summary>Matches nothing.</summary>
	public static readonly HighlightMatcher Empty = new([]);

	private readonly (HighlightRule Rule, Regex Regex)[] _rules;

	public HighlightMatcher(IEnumerable<HighlightRule> rules) =>
		_rules = rules
			.Where(rule => rule is { Enabled: true, HasEffect: true } && Validate(rule) is NotFound)
			.SelectMany(rule => Compile(rule) is Regex regex ? [(rule, regex)] : Array.Empty<(HighlightRule, Regex)>())
			.ToArray();

	/// <summary>True when no rule can match anything.</summary>
	public bool IsEmpty => _rules.Length == 0;

	/// <summary>What the rules do to a server line, or nothing when none matched.</summary>
	public Found<HighlightHit> Match(string text, TerminalLineComm comm)
	{
		var plain = text.TrimEnd('\r', '\n');
		HighlightRule? first = null;
		var line = HighlightLook.None;
		string? lineRewrite = null;
		var marks = new List<HighlightMark>();
		var sound = HighlightSound.None;
		var notify = false;
		var hide = false;
		foreach (var (rule, regex) in _rules)
		{
			if (!Applies(rule.Lines, comm) || Find(regex, plain) is not System.Text.RegularExpressions.Match match) continue;
			first ??= rule;
			if (sound == HighlightSound.None) sound = rule.Sound;
			notify |= rule.Notify;
			hide |= rule.Hide;
			var look = new HighlightLook(rule.Colour, rule.Paint, rule.Style);
			var rewrite = string.IsNullOrEmpty(rule.Rewrite) ? null : rule.Rewrite;
			if (rule.Target == HighlightTarget.Line)
			{
				if (line.IsNone) line = look;
				lineRewrite ??= rewrite is null ? null : Expand(match, rewrite);
			}
			else if (!look.IsNone || rewrite is not null)
				MarkAll(rule, match, look, rewrite, marks);
			if (rule.Stop) break;
		}
		if (first is null) return new NotFound();
		marks.Sort((a, b) => a.Start.CompareTo(b.Start));
		return new HighlightHit(first, line, marks, sound, notify, hide, lineRewrite);
	}

	/// <summary>Adds the part of each place <paramref name="match"/> and the ones after it are that the rule targets.</summary>
	private static void MarkAll(HighlightRule rule, Match match, HighlightLook look, string? rewrite, List<HighlightMark> marks)
	{
		for (var count = 0; count < MaxMarksPerRule && match.Success; count++)
		{
			Group part = rule.Target == HighlightTarget.Group ? match.Groups[rule.Group?.Trim() ?? string.Empty] : match;
			if (part.Success)
			{
				var mark = new HighlightMark(part.Index, part.Length, look, rewrite is null ? null : Expand(match, rewrite));
				if ((mark.Length > 0 || mark.Rewrite is not null) && !marks.Any(taken => Overlaps(taken, mark)))
					marks.Add(mark);
			}
			if (Next(match) is not System.Text.RegularExpressions.Match next) break;
			match = next;
		}
	}

	private static bool Overlaps(HighlightMark a, HighlightMark b) =>
		a.Length == 0 || b.Length == 0
			? a.Start == b.Start || (a.Start < b.Start && b.Start < a.End) || (b.Start < a.Start && a.Start < b.End)
			: a.Start < b.End && b.Start < a.End;

	/// <summary>
	/// The rewrite with what matched put in: <c>$1</c>, <c>${name}</c>, <c>$0</c>, and <c>$$</c> for a dollar sign. A
	/// rewrite the replacement syntax cannot read (a group number past the largest there can be) is shown as written.
	/// </summary>
	private static string Expand(Match match, string rewrite)
	{
		try
		{
			return match.Result(rewrite);
		}
		catch (ArgumentException)
		{
			return rewrite;
		}
	}

	/// <summary>Why the rule's pattern cannot be used, or nothing when it can.</summary>
	public static Found<HighlightProblem> Validate(HighlightRule rule)
	{
		if (string.IsNullOrWhiteSpace(rule.Pattern)) return HighlightProblem.Empty;
		if (rule.Pattern.Length > HighlightRule.MaxPatternLength) return HighlightProblem.TooLong;
		if (rule.Rewrite?.Length > HighlightRule.MaxRewriteLength) return HighlightProblem.RewriteTooLong;
		if (Compile(rule) is not Regex regex) return HighlightProblem.NotARegex;
		return rule.Target == HighlightTarget.Group && !HasGroup(regex, rule.Group) ? HighlightProblem.NoSuchGroup : new NotFound();
	}

	private static bool Applies(HighlightLines lines, TerminalLineComm comm) => (lines, comm) switch
	{
		(HighlightLines.All, _) => true,
		(HighlightLines.Pages, TerminalLineComm.Page) => true,
		(HighlightLines.Channels, TerminalLineComm.Channel) => true,
		(HighlightLines.Other, TerminalLineComm.None) => true,
		_ => false
	};

	private static bool HasGroup(Regex regex, string? group) =>
		!string.IsNullOrWhiteSpace(group) && regex.GroupNumberFromName(group.Trim()) >= 0;

	private static Found<Match> Find(Regex regex, string text)
	{
		try
		{
			return regex.Match(text) is { Success: true } match ? match : new NotFound();
		}
		catch (RegexMatchTimeoutException)
		{
			return new NotFound();
		}
	}

	private static Found<Match> Next(Match match)
	{
		try
		{
			return match.NextMatch() is { Success: true } next ? next : new NotFound();
		}
		catch (RegexMatchTimeoutException)
		{
			return new NotFound();
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
			HighlightMatch.Contains => Regex.Escape(rule.Pattern),
			HighlightMatch.StartsWith => "^" + Regex.Escape(rule.Pattern),
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

	/// <summary>
	/// A wildcard pattern over the whole line, as MUSH globs read: <c>*</c> and <c>?</c>, and <c>\</c> escapes. Each
	/// <c>*</c> and <c>?</c> is a group, numbered from 1, and a <c>*</c> takes as little as it can, as a
	/// <c>$</c>-command's does.
	/// </summary>
	private static string GlobPattern(string glob)
	{
		var pattern = new StringBuilder("^");
		for (var at = 0; at < glob.Length; at++)
		{
			var c = glob[at];
			pattern.Append(c switch
			{
				'*' => "((?s:.*?))",
				'?' => "((?s:.))",
				'\\' when at + 1 < glob.Length => Regex.Escape(glob[++at].ToString()),
				_ => Regex.Escape(c.ToString())
			});
		}
		return pattern.Append('$').ToString();
	}
}
