using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.Client.Services;

/// <summary><see cref="HighlightMatcher"/>: how a rule's pattern is read, which lines it looks at, and what wins.</summary>
public class HighlightMatcherTests
{
	private static HighlightRule Rule(string pattern, HighlightMatch match = HighlightMatch.Words, bool caseSensitive = false,
		HighlightLines lines = HighlightLines.All, HighlightColour colour = HighlightColour.Accent,
		HighlightSound sound = HighlightSound.None, bool notify = false, bool enabled = true) =>
		new(pattern, pattern, match, caseSensitive, lines, colour, sound, notify, enabled);

	private static Found<HighlightHit> Match(HighlightRule rule, string text, TerminalLineComm comm = TerminalLineComm.None) =>
		new HighlightMatcher([rule]).Match(text, comm);

	[Test]
	[Arguments("Ann waves.", true)]
	[Arguments("ann waves.", true)]
	[Arguments("Hello, Ann!", true)]
	[Arguments("Anne waves.", false)]
	[Arguments("Joanna waves.", false)]
	[Arguments("Ann_2 waves.", false)]
	public async Task Words_match_only_as_a_whole_word(string line, bool matches)
	{
		await Assert.That(Match(Rule("Ann"), line) is HighlightHit).IsEqualTo(matches);
	}

	[Test]
	public async Task A_phrase_matches_across_any_run_of_white_space()
	{
		await Assert.That(Match(Rule("red  dragon"), "A red   dragon lands.") is HighlightHit).IsTrue();
		await Assert.That(Match(Rule("red dragon"), "A reddragon lands.") is HighlightHit).IsFalse();
	}

	[Test]
	public async Task Words_are_literal_not_a_pattern()
	{
		await Assert.That(Match(Rule("a.b"), "axb") is HighlightHit).IsFalse();
		await Assert.That(Match(Rule("a.b"), "see a.b now") is HighlightHit).IsTrue();
	}

	[Test]
	public async Task Match_case_tells_letters_apart()
	{
		var rule = Rule("Ann", caseSensitive: true);
		await Assert.That(Match(rule, "Ann waves.") is HighlightHit).IsTrue();
		await Assert.That(Match(rule, "ann waves.") is HighlightHit).IsFalse();
	}

	[Test]
	[Arguments("* pages: *", "Ann pages: hello", true)]
	[Arguments("* pages: *", "Ann says, \"pages: hello\"", false)]
	[Arguments("?nn waves.", "Ann waves.", true)]
	[Arguments("\\*news\\*", "*news*", true)]
	[Arguments("\\*news\\*", "xnewsx", false)]
	public async Task A_glob_covers_the_whole_line(string glob, string line, bool matches)
	{
		await Assert.That(Match(Rule(glob, HighlightMatch.Glob), line) is HighlightHit).IsEqualTo(matches);
	}

	[Test]
	public async Task A_regex_is_found_anywhere()
	{
		var rule = Rule(@"\bOOC\b", HighlightMatch.Regex);
		await Assert.That(Match(rule, "<OOC> Ann says hi") is HighlightHit).IsTrue();
		await Assert.That(Match(rule, "Snooc") is HighlightHit).IsFalse();
	}

	[Test]
	public async Task A_trailing_line_ending_does_not_stop_an_anchored_match()
	{
		await Assert.That(Match(Rule("*waves.", HighlightMatch.Glob), "Ann waves.\r\n") is HighlightHit).IsTrue();
	}

	[Test]
	[Arguments(HighlightLines.All, TerminalLineComm.Page, true)]
	[Arguments(HighlightLines.Pages, TerminalLineComm.Page, true)]
	[Arguments(HighlightLines.Pages, TerminalLineComm.Channel, false)]
	[Arguments(HighlightLines.Channels, TerminalLineComm.Channel, true)]
	[Arguments(HighlightLines.Channels, TerminalLineComm.None, false)]
	[Arguments(HighlightLines.Other, TerminalLineComm.None, true)]
	[Arguments(HighlightLines.Other, TerminalLineComm.Page, false)]
	public async Task A_rule_looks_only_at_the_lines_it_names(HighlightLines lines, TerminalLineComm comm, bool matches)
	{
		await Assert.That(Match(Rule("Ann", lines: lines), "Ann waves.", comm) is HighlightHit).IsEqualTo(matches);
	}

	[Test]
	public async Task The_first_colour_and_sound_win_and_any_rule_can_notify()
	{
		var matcher = new HighlightMatcher(
		[
			Rule("Ann", colour: HighlightColour.Danger),
			Rule("waves", colour: HighlightColour.Info, sound: HighlightSound.Ping),
			Rule("Ann", colour: HighlightColour.None, sound: HighlightSound.Knock, notify: true)
		]);

		var hit = matcher.Match("Ann waves.", TerminalLineComm.None).Expect<HighlightHit>();

		await Assert.That(hit.Rule.Pattern).IsEqualTo("Ann");
		await Assert.That(hit.Colour).IsEqualTo(HighlightColour.Danger);
		await Assert.That(hit.Sound).IsEqualTo(HighlightSound.Ping);
		await Assert.That(hit.Notify).IsTrue();
	}

	[Test]
	public async Task An_off_rule_or_one_that_does_nothing_is_left_out()
	{
		await Assert.That(Match(Rule("Ann", enabled: false), "Ann waves.") is HighlightHit).IsFalse();
		await Assert.That(Match(Rule("Ann", colour: HighlightColour.None), "Ann waves.") is HighlightHit).IsFalse();
		await Assert.That(new HighlightMatcher([Rule("Ann", enabled: false)]).IsEmpty).IsTrue();
	}

	[Test]
	public async Task A_regex_that_does_not_compile_is_refused_and_left_out()
	{
		var rule = Rule("(unclosed", HighlightMatch.Regex);

		await Assert.That(HighlightMatcher.Validate(rule) is HighlightProblem.NotARegex).IsTrue();
		await Assert.That(new HighlightMatcher([rule]).IsEmpty).IsTrue();
	}

	[Test]
	public async Task An_empty_or_overlong_pattern_is_refused()
	{
		await Assert.That(HighlightMatcher.Validate(Rule("  ")) is HighlightProblem.Empty).IsTrue();
		await Assert.That(HighlightMatcher.Validate(Rule(new string('a', HighlightRule.MaxPatternLength + 1))) is HighlightProblem.TooLong).IsTrue();
		await Assert.That(HighlightMatcher.Validate(Rule("Ann")) is NotFound).IsTrue();
	}

	[Test]
	public async Task A_regex_that_runs_away_counts_as_no_match()
	{
		var rule = Rule("(a+)+$", HighlightMatch.Regex);
		var line = new string('a', 5000) + "!";

		await Assert.That(Match(rule, line) is HighlightHit).IsFalse();
	}

	[Test]
	public async Task Stored_rules_that_cannot_be_used_are_left_out_and_the_rest_kept()
	{
		var stored = System.Text.Json.JsonDocument.Parse("""
			[
			  { "id": "a", "pattern": "Ann", "colour": "Warn" },
			  { "id": "b" },
			  { "id": "c", "pattern": "Bob", "sound": "Siren" },
			  { "id": "d", "pattern": "Cy", "colour": 99 },
			  { "id": "a", "pattern": "Again" },
			  "not a rule",
			  { "id": "e", "pattern": "Dee", "match": "Glob", "lines": "Pages", "notify": true }
			]
			""").RootElement.EnumerateArray().ToArray();

		var rules = HighlightRules.Read(stored);

		await Assert.That(rules.Select(r => r.Id)).IsEquivalentTo(new[] { "a", "e" });
		await Assert.That(rules[0].Colour).IsEqualTo(HighlightColour.Warn);
		await Assert.That(rules[1]).IsEqualTo(new HighlightRule("e", "Dee", HighlightMatch.Glob, false, HighlightLines.Pages,
			HighlightColour.Accent, HighlightSound.None, true, true));
	}
}
