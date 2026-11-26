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
		await Assert.That(hit.Line.Colour).IsEqualTo(HighlightColour.Danger);
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

	[Test]
	public async Task Stored_rules_keep_their_target_style_and_advanced_choices()
	{
		var stored = System.Text.Json.JsonDocument.Parse("""
			[
			  { "id": "a", "pattern": "(\\w+) pages", "match": "Regex", "target": "Group", "group": "1", "paint": "Text",
			    "style": "Bold, Underline", "hide": true, "rewrite": "[$1]", "stop": true },
			  { "id": "b", "pattern": "Bob", "style": 99 },
			  { "id": "c", "pattern": "Cy", "target": "Window" }
			]
			""").RootElement.EnumerateArray().ToArray();

		var rules = HighlightRules.Read(stored);

		await Assert.That(rules.Select(r => r.Id)).IsEquivalentTo(new[] { "a" });
		await Assert.That(rules[0]).IsEqualTo(new HighlightRule("a", @"(\w+) pages", HighlightMatch.Regex, Target: HighlightTarget.Group, Group: "1",
			Paint: HighlightPaint.Text, Style: HighlightStyle.Bold | HighlightStyle.Underline, Hide: true, Rewrite: "[$1]", Stop: true));
	}

	private static HighlightHit Hit(string line, params HighlightRule[] rules) =>
		new HighlightMatcher(rules).Match(line, TerminalLineComm.None).Expect<HighlightHit>();

	private static (int Start, int Length)[] Places(HighlightHit hit) => [.. hit.Marks.Select(m => (m.Start, m.Length))];

	[Test]
	[Arguments("Anne waves.", true)]
	[Arguments("Joanna waves.", true)]
	[Arguments("Bob waves.", false)]
	public async Task Contains_matches_inside_a_longer_word(string line, bool matches)
	{
		await Assert.That(Match(Rule("ann", HighlightMatch.Contains), line) is HighlightHit).IsEqualTo(matches);
	}

	[Test]
	public async Task Starts_with_matches_only_at_the_start_of_the_line()
	{
		var rule = Rule("<OOC>", HighlightMatch.StartsWith);
		await Assert.That(Match(rule, "<OOC> Ann: hi") is HighlightHit).IsTrue();
		await Assert.That(Match(rule, "Ann says <OOC>") is HighlightHit).IsFalse();
	}

	[Test]
	public async Task A_rule_that_targets_the_match_marks_every_place_it_matches_and_not_the_line()
	{
		var hit = Hit("Ann waves to Ann.", Rule("Ann") with { Target = HighlightTarget.Match, Colour = HighlightColour.Info });

		await Assert.That(hit.Line.IsNone).IsTrue();
		await Assert.That(Places(hit)).IsEquivalentTo(new[] { (0, 3), (13, 3) });
		await Assert.That(hit.Marks[0].Look).IsEqualTo(new HighlightLook(HighlightColour.Info, HighlightPaint.Tint, HighlightStyle.None));
	}

	[Test]
	public async Task A_rule_that_targets_a_group_marks_only_that_group()
	{
		var numbered = Rule(@"(\w+) pages: (.*)", HighlightMatch.Regex) with { Target = HighlightTarget.Group, Group = "2" };
		var named = Rule(@"^(?<who>\w+) says", HighlightMatch.Regex) with { Target = HighlightTarget.Group, Group = "who" };
		var glob = Rule("* pages: *", HighlightMatch.Glob) with { Target = HighlightTarget.Group, Group = "1" };

		await Assert.That(Places(Hit("Ann pages: hello", numbered))).IsEquivalentTo(new[] { (11, 5) });
		await Assert.That(Places(Hit("Ann says hi", named))).IsEquivalentTo(new[] { (0, 3) });
		// A wildcard's * takes as little as it can, so group 1 is the first name.
		await Assert.That(Places(Hit("Ann pages: Bob pages: hi", glob))).IsEquivalentTo(new[] { (0, 3) });
	}

	[Test]
	public async Task A_group_the_pattern_does_not_have_is_refused()
	{
		var group = new HighlightRule("g", "Ann", Target: HighlightTarget.Group, Group: "1");

		await Assert.That(HighlightMatcher.Validate(group) is HighlightProblem.NoSuchGroup).IsTrue();
		await Assert.That(HighlightMatcher.Validate(group with { Match = HighlightMatch.Regex, Pattern = "(Ann)" }) is NotFound).IsTrue();
		await Assert.That(HighlightMatcher.Validate(group with { Match = HighlightMatch.Regex, Pattern = "(?<who>Ann)", Group = "who" }) is NotFound).IsTrue();
		await Assert.That(HighlightMatcher.Validate(group with { Match = HighlightMatch.Glob, Pattern = "* waves", Group = "2" }) is HighlightProblem.NoSuchGroup).IsTrue();
	}

	[Test]
	public async Task A_rewrite_puts_in_what_the_groups_matched()
	{
		var part = Rule(@"<(\w+)>", HighlightMatch.Regex) with { Target = HighlightTarget.Match, Rewrite = "[$1]" };
		var line = Rule(@"^(\w+) pages: (.*)$", HighlightMatch.Regex) with { Rewrite = "$1 → you: $2", Colour = HighlightColour.Warn };

		var marked = Hit("<OOC> Ann: hi <Public>", part);
		await Assert.That(marked.Marks.Select(m => m.Rewrite)).IsEquivalentTo(new string?[] { "[OOC]", "[Public]" });
		await Assert.That(Places(marked)).IsEquivalentTo(new[] { (0, 5), (14, 8) });

		var whole = Hit("Ann pages: hello", line);
		await Assert.That(whole.Rewrite).IsEqualTo("Ann → you: hello");
		await Assert.That(whole.Line.Colour).IsEqualTo(HighlightColour.Warn);
	}

	[Test]
	public async Task A_later_rules_part_that_overlaps_an_earlier_ones_is_left_out()
	{
		var hit = Hit("Ann waves.",
			Rule("Ann waves") with { Target = HighlightTarget.Match, Colour = HighlightColour.Info },
			Rule("waves") with { Target = HighlightTarget.Match, Colour = HighlightColour.Danger },
			Rule("Ann") with { Target = HighlightTarget.Match, Colour = HighlightColour.Danger });

		await Assert.That(Places(hit)).IsEquivalentTo(new[] { (0, 9) });
		await Assert.That(hit.Marks[0].Look.Colour).IsEqualTo(HighlightColour.Info);
	}

	[Test]
	public async Task Skip_later_rules_stops_at_the_first_rule_that_matches()
	{
		var matcher = new HighlightMatcher(
		[
			Rule("Bob", colour: HighlightColour.None) with { Stop = true },
			Rule("waves", sound: HighlightSound.Ping)
		]);

		var stopped = matcher.Match("Bob waves.", TerminalLineComm.None).Expect<HighlightHit>();
		await Assert.That(stopped.Sound).IsEqualTo(HighlightSound.None);
		await Assert.That(stopped.Line.IsNone).IsTrue();
		await Assert.That(matcher.Match("Ann waves.", TerminalLineComm.None).Expect<HighlightHit>().Sound).IsEqualTo(HighlightSound.Ping);
	}

	[Test]
	public async Task A_rule_that_only_styles_or_hides_still_has_an_effect()
	{
		await Assert.That(Match(Rule("Ann", colour: HighlightColour.None) with { Style = HighlightStyle.Bold }, "Ann waves.") is HighlightHit).IsTrue();
		var hidden = Hit("Ann waves.", Rule("Ann", colour: HighlightColour.None) with { Hide = true });
		await Assert.That(hidden.Hide).IsTrue();
	}

	[Test]
	public async Task An_overlong_rewrite_is_refused()
	{
		var rule = Rule("Ann") with { Rewrite = new string('x', HighlightRule.MaxRewriteLength + 1) };
		await Assert.That(HighlightMatcher.Validate(rule) is HighlightProblem.RewriteTooLong).IsTrue();
	}

	[Test]
	public async Task A_rewrite_the_replacement_syntax_cannot_read_is_shown_as_written()
	{
		var rule = Rule("*", HighlightMatch.Glob) with { Rewrite = "$2147483648" };

		var hit = Match(rule, "Ann waves.").Expect<HighlightHit>();

		await Assert.That(hit.Rewrite).IsEqualTo("$2147483648");
	}

	[Test]
	public async Task A_stored_rule_that_fails_validation_is_left_out_and_the_others_kept()
	{
		var tooLong = Rule("Ann") with { Rewrite = new string('x', HighlightRule.MaxRewriteLength + 1) };
		var matcher = new HighlightMatcher([tooLong, Rule("Bob")]);

		await Assert.That(matcher.Match("Ann waves.", TerminalLineComm.None) is HighlightHit).IsFalse();
		await Assert.That(matcher.Match("Bob waves.", TerminalLineComm.None) is HighlightHit).IsTrue();
	}
}
