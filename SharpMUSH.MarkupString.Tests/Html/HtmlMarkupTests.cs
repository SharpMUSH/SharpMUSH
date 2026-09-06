using MarkupString.Ansi;
using MarkupString.Html;
namespace SharpMUSH.MarkupString.Tests.Html;

/// <summary>
/// Tests for <see cref="HtmlMarkup"/>: verbatim tag rendering in Html/Pueblo/Mxp, folding of a
/// handful of tags into ANSI styling (Ansi/BBCode), passthrough elsewhere, the codec, and equality.
/// </summary>
public class HtmlMarkupTests
{
	private const string Esc = "\u001b";

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));

	private static string Render(MarkupText text, MarkupFormat format) => text.Render(format, Registry);

	// ── Html/Pueblo/Mxp: verbatim tag ───────────────────────────────────────────

	[Test]
	public async Task SendTag_RendersVerbatimInHtmlPuebloMxp_AndBareInAnsiAndPlain()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("send", "href=\"n\""), "north");

		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo("north");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo("<send href=\"n\">north</send>");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo("<send href=\"n\">north</send>");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("<send href=\"n\">north</send>");
		await Assert.That(Render(text, MarkupFormat.Plain)).IsEqualTo("north");
	}

	[Test]
	public async Task SimpleTag_NoAttributes_OmitsTheSpace()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("b"), "Bold Text");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("<b>Bold Text</b>");
	}

	[Test]
	public async Task DivWithClass_RendersAttributes()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("div", "class=\"container\""), "hi");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("<div class=\"container\">hi</div>");
	}

	[Test]
	public async Task NestedHtmlTags_WrapInnermostFirst()
	{
		var inner = MarkupText.Wrap(HtmlMarkup.Create("span", "class=\"inner\""), "Inner");
		var outer = MarkupText.Wrap(HtmlMarkup.Create("div", "class=\"outer\""), inner);
		await Assert.That(Render(outer, MarkupFormat.Html))
			.IsEqualTo("<div class=\"outer\"><span class=\"inner\">Inner</span></div>");
	}

	[Test]
	public async Task EmptyTag_StillWritesOpenAndClose()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("br"), "");
		// Wrap over empty text yields Empty; there is nothing to assert a run's tag on.
		await Assert.That(text.ToPlainText()).IsEqualTo("");
	}

	// ── Ansi/BBCode: fold into terminal styling ─────────────────────────────────

	[Test]
	public async Task BoldTag_FoldsIntoAnsiWhenAlone()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("b"), "x");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[1mx{Esc}[0m");
	}

	[Test]
	public async Task ItalicTag_FoldsIntoAnsi()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("em"), "x");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[3mx{Esc}[0m");
	}

	[Test]
	public async Task UnderlineTag_FoldsIntoAnsi()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("u"), "x");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[4mx{Esc}[0m");
	}

	[Test]
	public async Task StrikeTag_FoldsIntoAnsi()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("del"), "x");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[9mx{Esc}[0m");
	}

	[Test]
	public async Task BoldInsideRed_FoldsIntoOneSgrSequence()
	{
		var text = MarkupText.Wrap(Red, MarkupText.Wrap(HtmlMarkup.Create("b"), "x"));

		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[1;31mx{Esc}[0m");

		// Html: "b" does not claim a style there, so it is delegated to HtmlTagEmitter and wraps the
		// folded <span> from outside — the same nesting AnsiForeignLayerTests establishes for any
		// layer that answers false for a format (see Html_FormatSpecificStyleSourceWithAnsiLayer_
		// TagWrapsTheColourSpan, same Wrap(Red, Wrap(<falls-back-tag>, "x")) shape).
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<b><span style=\"color: #aa0000\">x</span></b>");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("[color=#aa0000][b]x[/b][/color]");
	}

	[Test]
	public async Task UnknownTag_PassesBodyThroughInAnsi()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("div"), "x");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo("x");
	}

	// ── Plain/BBCode passthrough for the tag itself ─────────────────────────────

	[Test]
	public async Task BoldTag_Plain_IsBareText()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("b"), "x");
		await Assert.That(Render(text, MarkupFormat.Plain)).IsEqualTo("x");
	}

	[Test]
	public async Task BoldTag_BBCode_UsesBBCodeBoldTag()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("b"), "x");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("[b]x[/b]");
	}

	[Test]
	public async Task UnknownTag_BBCode_HasNoRoute_PassesBodyThrough()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("div"), "x");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("x");
	}

	// ── Concatenation / coalescing ───────────────────────────────────────────────

	[Test]
	public async Task AdjacentSameTag_CoalescesIntoOneRun()
	{
		var first = MarkupText.Wrap(HtmlMarkup.Create("b"), "Hello ");
		var second = MarkupText.Wrap(HtmlMarkup.Create("b"), "World");
		var combined = MarkupText.Concat(first, second);

		await Assert.That(combined.Runs.Length).IsEqualTo(1);
		await Assert.That(Render(combined, MarkupFormat.Html)).IsEqualTo("<b>Hello World</b>");
	}

	[Test]
	public async Task DifferentTags_DoNotCoalesce()
	{
		var first = MarkupText.Wrap(HtmlMarkup.Create("b"), "Bold ");
		var second = MarkupText.Wrap(HtmlMarkup.Create("i"), "Italic");
		var combined = MarkupText.Concat(first, second);

		await Assert.That(combined.Runs.Length).IsEqualTo(2);
		await Assert.That(Render(combined, MarkupFormat.Html)).IsEqualTo("<b>Bold </b><i>Italic</i>");
	}

	// ── Substring/split preserve markup ─────────────────────────────────────────

	[Test]
	public async Task Substring_PreservesTheTag()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("b"), "Hello, World!");
		var result = text.Substring(7, 5);

		await Assert.That(result.ToPlainText()).IsEqualTo("World");
		await Assert.That(Render(result, MarkupFormat.Html)).IsEqualTo("<b>World</b>");
	}

	[Test]
	public async Task Split_PreservesTheTagOnEachPiece()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("span"), "one,two,three");
		var pieces = text.Split(",");

		await Assert.That(pieces.Length).IsEqualTo(3);
		await Assert.That(Render(pieces[0], MarkupFormat.Html)).IsEqualTo("<span>one</span>");
		await Assert.That(Render(pieces[1], MarkupFormat.Html)).IsEqualTo("<span>two</span>");
		await Assert.That(Render(pieces[2], MarkupFormat.Html)).IsEqualTo("<span>three</span>");
	}

	// ── Codec / serialization round trip ────────────────────────────────────────

	[Test]
	public async Task Codec_RoundTrips_TagWithoutAttributes()
	{
		var original = MarkupText.Wrap(HtmlMarkup.Create("b"), "Test HTML");
		var json = MarkupTextSerializer.Serialize(original, Registry);
		var deserialized = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(deserialized.ToPlainText()).IsEqualTo(original.ToPlainText());
		await Assert.That(Render(deserialized, MarkupFormat.Html)).IsEqualTo(Render(original, MarkupFormat.Html));
	}

	[Test]
	public async Task Codec_RoundTrips_TagWithAttributes()
	{
		var original = MarkupText.Wrap(HtmlMarkup.Create("a", "href=\"https://test.com\""), "click");
		var json = MarkupTextSerializer.Serialize(original, Registry);
		var deserialized = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(Render(deserialized, MarkupFormat.Html)).IsEqualTo(Render(original, MarkupFormat.Html));
		await Assert.That(Render(deserialized, MarkupFormat.Html)).Contains("href=\"https://test.com\"");
	}

	[Test]
	public async Task Codec_UsesCompactKeys_AndOmitsEmptyAttributes()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("b"), "x");
		await Assert.That(MarkupTextSerializer.Serialize(text, Registry))
			.IsEqualTo("""{"t":"x","p":[null,[{"k":"html","h":"b"}]],"r":[1,1]}""");
	}

	[Test]
	public async Task Codec_WritesAttributesWhenPresent()
	{
		var text = MarkupText.Wrap(HtmlMarkup.Create("a", "href=\"x\""), "x");
		await Assert.That(MarkupTextSerializer.Serialize(text, Registry)).Contains("\"a\":\"href=\\\"x\\\"\"");
	}

	// ── Equality ─────────────────────────────────────────────────────────────────

	[Test]
	public async Task Equality_SameTagAndAttributes_AreEqual()
	{
		await Assert.That(HtmlMarkup.Create("b")).IsEqualTo(HtmlMarkup.Create("b"));
		await Assert.That(HtmlMarkup.Create("a", "href=\"x\"")).IsEqualTo(HtmlMarkup.Create("a", "href=\"x\""));
	}

	[Test]
	public async Task Equality_DifferentTagOrAttributes_AreNotEqual()
	{
		await Assert.That(HtmlMarkup.Create("b")).IsNotEqualTo(HtmlMarkup.Create("i"));
		await Assert.That(HtmlMarkup.Create("a", "href=\"x\"")).IsNotEqualTo(HtmlMarkup.Create("a", "href=\"y\""));
	}
}
