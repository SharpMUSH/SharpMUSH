using System.Collections.Immutable;
using System.Drawing;
using ANSILibrary;
using MarkupString;
using MarkupString.MarkupImplementation;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Tests that Pueblo/MXP rendering is additive: ANSI escape codes for colors/styles
/// plus HTML tags for semantic markup (e.g. &lt;send&gt;).
/// </summary>
public class PuebloMxpRenderTests
{
	/// <summary>
	/// Plain text with no markup renders identically in all formats.
	/// </summary>
	[Test]
	public async Task PlainText_SameInAllFormats()
	{
		var mstr = MModule.single("Hello, world!");

		await Assert.That(mstr.Render("pueblo")).IsEqualTo("Hello, world!");
		await Assert.That(mstr.Render("mxp")).IsEqualTo("Hello, world!");
		await Assert.That(mstr.Render("ansi")).IsEqualTo("Hello, world!");
	}

	/// <summary>
	/// ANSI color markup renders as ANSI escape codes in Pueblo/MXP mode (not HTML tags).
	/// </summary>
	[Test]
	public async Task AnsiColor_PreservedInPuebloMode()
	{
		// Create an MString with red foreground
		var red = AnsiMarkup.Create(foreground: new AnsiColor.RGB(Color.Red));
		var mstr = MModule.MarkupSingle(red, "Red text");

		var pueblo = mstr.Render("pueblo");
		var ansi = mstr.Render("ansi");

		// Pueblo should produce ANSI escape codes, not <FONT> HTML
		await Assert.That(pueblo).Contains("\x1b[");
		await Assert.That(pueblo).DoesNotContain("<FONT");
		await Assert.That(pueblo).DoesNotContain("<span");

		// Should be the same as ANSI rendering for color-only markup
		await Assert.That(pueblo).IsEqualTo(ansi);
	}

	/// <summary>
	/// HtmlMarkup (e.g. &lt;send&gt;) renders as HTML tags in Pueblo/MXP mode.
	/// </summary>
	[Test]
	public async Task HtmlSendTag_RenderedInPuebloMode()
	{
		var send = HtmlMarkup.Create("send", "href=\"North\" hint=\"North|n|no\"");
		var mstr = MModule.MarkupSingle(send, "North");

		var pueblo = mstr.Render("pueblo");
		var ansiOutput = mstr.Render("ansi");

		// Pueblo/MXP should include the <send> tag
		await Assert.That(pueblo).Contains("<send href=\"North\" hint=\"North|n|no\">North</send>");

		// ANSI should NOT include any HTML tags — HtmlMarkup with unknown tag just passes text through
		await Assert.That(ansiOutput).DoesNotContain("<send");
		await Assert.That(ansiOutput).StartsWith("North");
	}

	/// <summary>
	/// Combined ANSI + HTML markup: Pueblo/MXP produces ANSI codes wrapping HTML-tagged text.
	/// </summary>
	[Test]
	public async Task CombinedAnsiAndHtml_AdditiveInPuebloMode()
	{
		// Build: red-colored text with a <send> tag
		var red = AnsiMarkup.Create(foreground: new AnsiColor.RGB(Color.Red));
		var send = HtmlMarkup.Create("send", "href=\"North\"");
		var mstr = MModule.MarkupSingleMulti(
			ImmutableArray.Create<IMarkup>(red, send), "North");

		var pueblo = mstr.Render("pueblo");

		// Should have both ANSI escape codes AND HTML <send> tags
		await Assert.That(pueblo).Contains("\x1b[");
		await Assert.That(pueblo).Contains("<send href=\"North\">");
		await Assert.That(pueblo).Contains("</send>");
	}

	/// <summary>
	/// MXP format produces the same output as Pueblo (both are additive ANSI+HTML).
	/// </summary>
	[Test]
	public async Task Mxp_SameAsPueblo()
	{
		var send = HtmlMarkup.Create("send", "href=\"look\"");
		var mstr = MModule.MarkupSingle(send, "look here");

		var pueblo = mstr.Render("pueblo");
		var mxp = mstr.Render("mxp");

		await Assert.That(mxp).IsEqualTo(pueblo);
	}

	/// <summary>
	/// Bold ANSI markup renders as ANSI bold escape in Pueblo mode, not &lt;B&gt; tag.
	/// </summary>
	[Test]
	public async Task AnsiBold_PreservedAsAnsiInPuebloMode()
	{
		var bold = AnsiMarkup.Create(bold: true);
		var mstr = MModule.MarkupSingle(bold, "Important");

		var pueblo = mstr.Render("pueblo");

		await Assert.That(pueblo).Contains("\x1b[");
		await Assert.That(pueblo).DoesNotContain("<B>");
		await Assert.That(pueblo).DoesNotContain("<b>");
	}
}
