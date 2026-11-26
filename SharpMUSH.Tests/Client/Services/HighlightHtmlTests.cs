using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.Client.Services;

/// <summary><see cref="HighlightHtml"/>: a rule's marks and rewrites put into a line's HTML, its tags kept.</summary>
public class HighlightHtmlTests
{
	private static readonly HighlightLook Info = new(HighlightColour.Info, HighlightPaint.Tint, HighlightStyle.None);
	private static readonly HighlightLook BoldText = new(HighlightColour.Warn, HighlightPaint.Text, HighlightStyle.Bold);

	private static HighlightHit Hit(params HighlightMark[] marks) =>
		new(new HighlightRule("r", "x"), HighlightLook.None, marks, HighlightSound.None, false);

	[Test]
	public async Task A_mark_wraps_its_text_and_counts_an_entity_as_one_character()
	{
		const string text = "<Ann> & Bob";
		const string html = "&lt;Ann&gt; &amp; Bob";

		var marked = HighlightHtml.Apply(html, text, Hit(new HighlightMark(8, 3, Info, null))).Expect<string>();

		await Assert.That(marked).IsEqualTo("&lt;Ann&gt; &amp; <span class=\"terminal-mark terminal-hl-info\">Bob</span>");
	}

	[Test]
	public async Task A_mark_closes_before_a_tag_and_opens_again_after_it()
	{
		const string html = "Ann <span style=\"color: red\">waves</span> hi";

		var marked = HighlightHtml.Apply(html, "Ann waves hi", Hit(new HighlightMark(2, 5, BoldText, null))).Expect<string>();

		await Assert.That(marked).IsEqualTo(
			"An<span class=\"terminal-hl-text terminal-hl-warn terminal-hl-bold\">n </span><span style=\"color: red\">"
			+ "<span class=\"terminal-hl-text terminal-hl-warn terminal-hl-bold\">wav</span>es</span> hi");
	}

	[Test]
	public async Task A_rewrite_replaces_its_text_and_keeps_the_tags_around_it()
	{
		const string html = "<OOC> hi<audio class=\"ms-sound\" src=\"x\"></audio>";
		var hit = Hit(new HighlightMark(0, 5, HighlightLook.None, "[OOC] & co"));

		var marked = HighlightHtml.Apply(html.Replace("<OOC>", "&lt;OOC&gt;"), "<OOC> hi", hit).Expect<string>();

		await Assert.That(marked).IsEqualTo("[OOC] &amp; co hi<audio class=\"ms-sound\" src=\"x\"></audio>");
	}

	[Test]
	public async Task A_rewrite_of_the_whole_line_takes_the_styling_of_its_first_text()
	{
		const string html = "<span class=\"ms-bold\">Ann</span> pages: hi";
		var hit = Hit() with { Rewrite = "Ann → you: hi" };

		var marked = HighlightHtml.Apply(html, "Ann pages: hi", hit).Expect<string>();

		await Assert.That(marked).IsEqualTo("<span class=\"ms-bold\">Ann → you: hi</span>");
	}

	[Test]
	public async Task Html_whose_text_is_not_the_line_places_no_marks()
	{
		const string html = "<img class=\"ms-image\" src=\"x\" alt=\"a map\">";

		await Assert.That(HighlightHtml.Apply(html, "a map", Hit(new HighlightMark(0, 1, Info, null))) is NotFound).IsTrue();
		await Assert.That(HighlightHtml.Apply(html, "a map", Hit() with { Rewrite = "<map>" }).Expect<string>()).IsEqualTo("&lt;map&gt;");
	}

	[Test]
	public async Task A_line_ending_in_the_text_does_not_stop_marks()
	{
		var marked = HighlightHtml.Apply("Ann waves.\n", "Ann waves.\r\n", Hit(new HighlightMark(0, 3, Info, null))).Expect<string>();

		await Assert.That(marked).IsEqualTo("<span class=\"terminal-mark terminal-hl-info\">Ann</span> waves.\n");
	}

	[Test]
	public async Task Nothing_to_mark_leaves_the_html_as_it_is()
	{
		await Assert.That(HighlightHtml.Apply("Ann", "Ann", Hit()).Expect<string>()).IsEqualTo("Ann");
	}

	[Test]
	public async Task An_entity_the_decoder_does_not_know_is_its_own_characters()
	{
		const string html = "&foo; hi";

		var marked = HighlightHtml.Apply(html, html, Hit(new HighlightMark(1, 3, HighlightLook.None, "bar"))).Expect<string>();

		await Assert.That(marked).IsEqualTo("&bar; hi");
	}

	[Test]
	public async Task A_rule_starting_at_the_line_marks_an_html_line_by_its_shown_text()
	{
		const string html = "<b title=\"Bob\">Ann</b> waves.";
		var text = HighlightHtml.TextOf(html);
		var rule = new HighlightRule("r", "^Ann", HighlightMatch.Regex) with { Target = HighlightTarget.Match };

		var hit = new HighlightMatcher([rule]).Match(text, TerminalLineComm.None).Expect<HighlightHit>();
		var marked = HighlightHtml.Apply(html, text, hit).Expect<string>();

		await Assert.That(text).IsEqualTo("Ann waves.");
		await Assert.That(marked).StartsWith("<b title=\"Bob\"><span class=\"terminal-mark");
		await Assert.That(new HighlightMatcher([new HighlightRule("r", "Bob", HighlightMatch.Contains)])
			.Match(text, TerminalLineComm.None) is HighlightHit).IsFalse();
	}

	[Test]
	public async Task A_less_than_sign_that_opens_no_tag_is_shown_text()
	{
		const string html = "Roll: 2 < 3 <b>ok</b> <3";
		var text = HighlightHtml.TextOf(html);
		var rule = new HighlightRule("r", "3", HighlightMatch.Contains) with { Target = HighlightTarget.Match };

		var hit = new HighlightMatcher([rule]).Match(text, TerminalLineComm.None).Expect<HighlightHit>();
		var marked = HighlightHtml.Apply(html, text, hit).Expect<string>();

		await Assert.That(text).IsEqualTo("Roll: 2 < 3 ok <3");
		await Assert.That(marked).StartsWith("Roll: 2 < <span class=\"terminal-mark");
	}
}
