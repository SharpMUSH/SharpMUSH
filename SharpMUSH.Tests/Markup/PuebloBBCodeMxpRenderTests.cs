using System.Drawing;
using Microsoft.FSharp.Core;
using A = MarkupString.MarkupStringModule;
using H = MarkupString.MarkupImplementation.HtmlMarkup;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Unit tests for Pueblo, BBCode, and MXP render format strategies.
/// </summary>
public class PuebloBBCodeMxpRenderTests
{
	// ── Pueblo Rendering ─────────────────────────────────────────────

	[Test]
	public async Task Render_PuebloFormat_ForegroundColor_ReturnsFontTag()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "pueblo_red_fg_unique");

		var result = markupString.Render("pueblo");

		await Assert.That(result).Contains("<FONT COLOR=\"#ff0000\">");
		await Assert.That(result).Contains("</FONT>");
		await Assert.That(result).Contains("pueblo_red_fg_unique");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_PuebloFormat_Bold_ReturnsBTag()
	{
		var ansiMarkup = M.Create(bold: true);
		var markupString = A.markupSingle(ansiMarkup, "pueblo_bold_unique");

		var result = markupString.Render("pueblo");

		await Assert.That(result).Contains("<B>");
		await Assert.That(result).Contains("</B>");
		await Assert.That(result).Contains("pueblo_bold_unique");
	}

	[Test]
	public async Task Render_PuebloFormat_Italic_ReturnsITag()
	{
		var ansiMarkup = M.Create(italic: true);
		var markupString = A.markupSingle(ansiMarkup, "pueblo_italic_unique");

		var result = markupString.Render("pueblo");

		await Assert.That(result).Contains("<I>");
		await Assert.That(result).Contains("</I>");
	}

	[Test]
	public async Task Render_PuebloFormat_Underline_ReturnsUTag()
	{
		var ansiMarkup = M.Create(underlined: true);
		var markupString = A.markupSingle(ansiMarkup, "pueblo_underline_unique");

		var result = markupString.Render("pueblo");

		await Assert.That(result).Contains("<U>");
		await Assert.That(result).Contains("</U>");
	}

	[Test]
	public async Task Render_PuebloFormat_BackgroundColor_ReturnsSpanStyle()
	{
		var ansiMarkup = M.Create(background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 128, 0)));
		var markupString = A.markupSingle(ansiMarkup, "pueblo_bg_unique");

		var result = markupString.Render("pueblo");

		await Assert.That(result).Contains("background-color: #008000");
		await Assert.That(result).Contains("pueblo_bg_unique");
	}

	[Test]
	public async Task Render_PuebloFormat_HtmlMarkup_RendersHtmlTags()
	{
		var htmlMarkup = H.Create("b");
		var markupString = A.markupSingle(htmlMarkup, "pueblo_html_bold_unique");

		var result = markupString.Render("pueblo");

		// HtmlMarkup renders its own tags in Pueblo format (Pueblo understands HTML)
		await Assert.That(result).IsEqualTo("<b>pueblo_html_bold_unique</b>");
	}

	[Test]
	public async Task Render_PuebloFormat_PlainText_ReturnsPlainText()
	{
		var markupString = A.single("pueblo_plain_unique");

		var result = markupString.Render("pueblo");

		await Assert.That(result).IsEqualTo("pueblo_plain_unique");
	}

	[Test]
	public async Task Render_PuebloFormat_EntityEncoding_EscapesHtmlChars()
	{
		var markupString = A.single("pueblo <script>alert('xss')</script> unique");

		var result = markupString.Render("pueblo");

		await Assert.That(result).Contains("&lt;script&gt;");
		await Assert.That(result).DoesNotContain("<script>");
	}

	[Test]
	public async Task Render_PuebloFormat_Inverted_SwapsColors()
	{
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)),
			inverted: true);
		var markupString = A.markupSingle(ansiMarkup, "pueblo_inverted_unique");

		var result = markupString.Render("pueblo");

		// When inverted: fg(red) becomes bg, bg(blue) becomes fg
		await Assert.That(result).Contains("<FONT COLOR=\"#0000ff\">");
		await Assert.That(result).Contains("background-color: #ff0000");
	}

	// ── BBCode Rendering ─────────────────────────────────────────────

	[Test]
	public async Task Render_BBCodeFormat_ForegroundColor_ReturnsColorTag()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "bbcode_red_fg_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).Contains("[color=#ff0000]");
		await Assert.That(result).Contains("[/color]");
		await Assert.That(result).Contains("bbcode_red_fg_unique");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_BBCodeFormat_Bold_ReturnsBTag()
	{
		var ansiMarkup = M.Create(bold: true);
		var markupString = A.markupSingle(ansiMarkup, "bbcode_bold_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).Contains("[b]");
		await Assert.That(result).Contains("[/b]");
		await Assert.That(result).Contains("bbcode_bold_unique");
	}

	[Test]
	public async Task Render_BBCodeFormat_Italic_ReturnsITag()
	{
		var ansiMarkup = M.Create(italic: true);
		var markupString = A.markupSingle(ansiMarkup, "bbcode_italic_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).Contains("[i]");
		await Assert.That(result).Contains("[/i]");
	}

	[Test]
	public async Task Render_BBCodeFormat_Underline_ReturnsUTag()
	{
		var ansiMarkup = M.Create(underlined: true);
		var markupString = A.markupSingle(ansiMarkup, "bbcode_underline_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).Contains("[u]");
		await Assert.That(result).Contains("[/u]");
	}

	[Test]
	public async Task Render_BBCodeFormat_StrikeThrough_ReturnsSTag()
	{
		var ansiMarkup = M.Create(strikeThrough: true);
		var markupString = A.markupSingle(ansiMarkup, "bbcode_strike_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).Contains("[s]");
		await Assert.That(result).Contains("[/s]");
	}

	[Test]
	public async Task Render_BBCodeFormat_Link_ReturnsUrlTag()
	{
		var ansiMarkup = M.Create(linkUrl: FSharpOption<string>.Some("https://example.com"));
		var markupString = A.markupSingle(ansiMarkup, "bbcode_link_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).Contains("[url=https://example.com]");
		await Assert.That(result).Contains("[/url]");
		await Assert.That(result).Contains("bbcode_link_unique");
	}

	[Test]
	public async Task Render_BBCodeFormat_PlainText_ReturnsPlainText()
	{
		var markupString = A.single("bbcode_plain_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).IsEqualTo("bbcode_plain_unique");
	}

	[Test]
	public async Task Render_BBCodeFormat_NoEntityEncoding_PreservesSpecialChars()
	{
		// BBCode does NOT HTML-encode text
		var markupString = A.single("bbcode a < b & c > d unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).IsEqualTo("bbcode a < b & c > d unique");
	}

	[Test]
	public async Task Render_BBCodeFormat_HtmlMarkup_StripsHtmlTags()
	{
		// HTML tags have no BBCode equivalent; text should pass through without tags
		var htmlMarkup = H.Create("b");
		var markupString = A.markupSingle(htmlMarkup, "bbcode_html_stripped_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).IsEqualTo("bbcode_html_stripped_unique");
		await Assert.That(result).DoesNotContain("<b>");
	}

	[Test]
	public async Task Render_BBCodeFormat_MultipleStyles_CombinesTags()
	{
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			bold: true,
			italic: true);
		var markupString = A.markupSingle(ansiMarkup, "bbcode_multi_unique");

		var result = markupString.Render("bbcode");

		await Assert.That(result).Contains("[color=#ff0000]");
		await Assert.That(result).Contains("[b]");
		await Assert.That(result).Contains("[i]");
		await Assert.That(result).Contains("bbcode_multi_unique");
	}

	// ── MXP Rendering ────────────────────────────────────────────────

	[Test]
	public async Task Render_MxpFormat_ForegroundColor_ReturnsColorTag()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "mxp_red_fg_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("<COLOR FORE=\"#ff0000\">");
		await Assert.That(result).Contains("</COLOR>");
		await Assert.That(result).Contains("mxp_red_fg_unique");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_MxpFormat_BackgroundColor_ReturnsColorBackTag()
	{
		var ansiMarkup = M.Create(background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 128, 0)));
		var markupString = A.markupSingle(ansiMarkup, "mxp_bg_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("<COLOR BACK=\"#008000\">");
		await Assert.That(result).Contains("</COLOR>");
	}

	[Test]
	public async Task Render_MxpFormat_ForegroundAndBackground_CombinesInColorTag()
	{
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)));
		var markupString = A.markupSingle(ansiMarkup, "mxp_fg_bg_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("<COLOR FORE=\"#ff0000\" BACK=\"#0000ff\">");
		await Assert.That(result).Contains("</COLOR>");
	}

	[Test]
	public async Task Render_MxpFormat_Bold_ReturnsBTag()
	{
		var ansiMarkup = M.Create(bold: true);
		var markupString = A.markupSingle(ansiMarkup, "mxp_bold_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("<B>");
		await Assert.That(result).Contains("</B>");
	}

	[Test]
	public async Task Render_MxpFormat_Italic_ReturnsITag()
	{
		var ansiMarkup = M.Create(italic: true);
		var markupString = A.markupSingle(ansiMarkup, "mxp_italic_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("<I>");
		await Assert.That(result).Contains("</I>");
	}

	[Test]
	public async Task Render_MxpFormat_Underline_ReturnsUTag()
	{
		var ansiMarkup = M.Create(underlined: true);
		var markupString = A.markupSingle(ansiMarkup, "mxp_underline_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("<U>");
		await Assert.That(result).Contains("</U>");
	}

	[Test]
	public async Task Render_MxpFormat_Link_ReturnsSendTag()
	{
		var ansiMarkup = M.Create(linkUrl: FSharpOption<string>.Some("https://example.com"));
		var markupString = A.markupSingle(ansiMarkup, "mxp_link_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("<SEND HREF=\"https://example.com\">");
		await Assert.That(result).Contains("</SEND>");
		await Assert.That(result).Contains("mxp_link_unique");
	}

	[Test]
	public async Task Render_MxpFormat_PlainText_ReturnsPlainText()
	{
		var markupString = A.single("mxp_plain_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).IsEqualTo("mxp_plain_unique");
	}

	[Test]
	public async Task Render_MxpFormat_EntityEncoding_EscapesHtmlChars()
	{
		var markupString = A.single("mxp <tag> & \"quoted\" unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).Contains("&lt;tag&gt;");
		await Assert.That(result).Contains("&amp;");
		await Assert.That(result).Contains("&quot;quoted&quot;");
	}

	[Test]
	public async Task Render_MxpFormat_HtmlMarkup_RendersHtmlTags()
	{
		// MXP understands HTML-like tags, so HtmlMarkup should render normally
		var htmlMarkup = H.Create("b");
		var markupString = A.markupSingle(htmlMarkup, "mxp_html_bold_unique");

		var result = markupString.Render("mxp");

		await Assert.That(result).IsEqualTo("<b>mxp_html_bold_unique</b>");
	}

	[Test]
	public async Task Render_MxpFormat_Inverted_SwapsColors()
	{
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)),
			inverted: true);
		var markupString = A.markupSingle(ansiMarkup, "mxp_inverted_unique");

		var result = markupString.Render("mxp");

		// When inverted: fg(red)→bg, bg(blue)→fg
		await Assert.That(result).Contains("FORE=\"#0000ff\"");
		await Assert.That(result).Contains("BACK=\"#ff0000\"");
	}

	// ── Cross-format Consistency ─────────────────────────────────────

	[Test]
	public async Task Render_AllFormats_PlainText_ReturnsUnchanged()
	{
		var markupString = A.single("cross_plain_text_unique");

		var ansi = markupString.Render("ansi");
		var html = markupString.Render("html");
		var pueblo = markupString.Render("pueblo");
		var bbcode = markupString.Render("bbcode");
		var mxp = markupString.Render("mxp");

		await Assert.That(ansi).IsEqualTo("cross_plain_text_unique");
		await Assert.That(html).IsEqualTo("cross_plain_text_unique");
		await Assert.That(pueblo).IsEqualTo("cross_plain_text_unique");
		await Assert.That(bbcode).IsEqualTo("cross_plain_text_unique");
		await Assert.That(mxp).IsEqualTo("cross_plain_text_unique");
	}

	[Test]
	public async Task Render_ConcatenatedMarkup_PuebloFormat_RendersEachSegment()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)));

		var redText = A.markupSingle(redMarkup, "pueblo_concat_red_unique");
		var blueText = A.markupSingle(blueMarkup, "pueblo_concat_blue_unique");
		var combined = A.concat(redText, blueText);

		var result = combined.Render("pueblo");

		await Assert.That(result).Contains("FONT COLOR=\"#ff0000\"");
		await Assert.That(result).Contains("FONT COLOR=\"#0000ff\"");
		await Assert.That(result).Contains("pueblo_concat_red_unique");
		await Assert.That(result).Contains("pueblo_concat_blue_unique");
	}

	[Test]
	public async Task Render_ModuleLevelFunction_PuebloFormat_Works()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 165, 0)));
		var markupString = A.markupSingle(ansiMarkup, "pueblo_module_orange_unique");

		var result = A.render("pueblo", markupString);

		await Assert.That(result).Contains("FONT COLOR=\"#ffa500\"");
		await Assert.That(result).Contains("pueblo_module_orange_unique");
	}

	[Test]
	public async Task Render_ModuleLevelFunction_BBCodeFormat_Works()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 165, 0)));
		var markupString = A.markupSingle(ansiMarkup, "bbcode_module_orange_unique");

		var result = A.render("bbcode", markupString);

		await Assert.That(result).Contains("[color=#ffa500]");
		await Assert.That(result).Contains("bbcode_module_orange_unique");
	}

	[Test]
	public async Task Render_ModuleLevelFunction_MxpFormat_Works()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 165, 0)));
		var markupString = A.markupSingle(ansiMarkup, "mxp_module_orange_unique");

		var result = A.render("mxp", markupString);

		await Assert.That(result).Contains("COLOR FORE=\"#ffa500\"");
		await Assert.That(result).Contains("mxp_module_orange_unique");
	}
}
