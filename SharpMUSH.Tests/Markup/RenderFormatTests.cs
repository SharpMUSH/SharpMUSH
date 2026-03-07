using System.Drawing;
using Microsoft.FSharp.Core;
using MarkupString;
using A = MarkupString.MarkupStringModule;
using H = MarkupString.MarkupImplementation.HtmlMarkup;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Unit tests for the format-aware Render method on MarkupString.
/// </summary>
public class RenderFormatTests
{
	[Test]
	public async Task Render_AnsiFormat_ReturnsAnsiOutput()
	{
		// Arrange
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var markupString = A.markupSingle(ansiMarkup, "Red Text");

		// Act
		var ansiResult = markupString.Render("ansi");
		var toStringResult = markupString.ToString();

		// Assert - "ansi" format should match ToString() behavior
		await Assert.That(ansiResult).IsEqualTo(toStringResult);
	}

	[Test]
	public async Task Render_HtmlFormat_ForegroundColor_ReturnsSpanWithInlineStyle()
	{
		// Arrange
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "Red Text");

		// Act
		var result = markupString.Render("html");

		// Assert - color via inline style, no class for color
		await Assert.That(result).Contains("<span style=\"color: #ff0000\">");
		await Assert.That(result).Contains("Red Text");
		await Assert.That(result).Contains("</span>");
		await Assert.That(result).DoesNotContain("fg-ff0000");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_HtmlFormat_BoldMarkup_ReturnsSpanWithBoldClass()
	{
		// Arrange
		var ansiMarkup = M.Create(bold: true);
		var markupString = A.markupSingle(ansiMarkup, "Bold Text");

		// Act
		var result = markupString.Render("html");

		// Assert - formatting flag uses class, and there is no inline style when no color
		await Assert.That(result).Contains("ms-bold");
		await Assert.That(result).Contains("Bold Text");
		await Assert.That(result).DoesNotContain("style=");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_HtmlFormat_ItalicMarkup_ReturnsSpanWithItalicClass()
	{
		// Arrange
		var ansiMarkup = M.Create(italic: true);
		var markupString = A.markupSingle(ansiMarkup, "Italic Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("ms-italic");
		await Assert.That(result).Contains("Italic Text");
	}

	[Test]
	public async Task Render_HtmlFormat_UnderlinedMarkup_ReturnsSpanWithUnderlineClass()
	{
		// Arrange
		var ansiMarkup = M.Create(underlined: true);
		var markupString = A.markupSingle(ansiMarkup, "Underlined Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("ms-underline");
		await Assert.That(result).Contains("Underlined Text");
	}

	[Test]
	public async Task Render_HtmlFormat_BackgroundColor_ReturnsSpanWithInlineStyle()
	{
		// Arrange
		var ansiMarkup = M.Create(background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 128, 0)));
		var markupString = A.markupSingle(ansiMarkup, "Green BG");

		// Act
		var result = markupString.Render("html");

		// Assert - background color via inline style
		await Assert.That(result).Contains("background-color: #008000");
		await Assert.That(result).Contains("Green BG");
		await Assert.That(result).DoesNotContain("bg-008000");
	}

	[Test]
	public async Task Render_HtmlFormat_HtmlMarkup_ReturnsHtmlTags()
	{
		// Arrange
		var htmlMarkup = H.Create("b");
		var markupString = A.markupSingle(htmlMarkup, "Bold HTML");

		// Act
		var result = markupString.Render("html");

		// Assert - HtmlMarkup renders the same regardless of format
		await Assert.That(result).IsEqualTo("<b>Bold HTML</b>");
	}

	[Test]
	public async Task Render_HtmlFormat_PlainText_ReturnsPlainText()
	{
		// Arrange
		var markupString = A.single("Plain Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).IsEqualTo("Plain Text");
	}

	[Test]
	public async Task Render_HtmlFormat_MultipleStyles_CombinesIntoOneSpan()
	{
		// Arrange
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			bold: true,
			italic: true);
		var markupString = A.markupSingle(ansiMarkup, "Styled Text");

		// Act
		var result = markupString.Render("html");

		// Assert - color via inline style, formatting via class, both in one span
		await Assert.That(result).Contains("color: #ff0000");
		await Assert.That(result).Contains("ms-bold");
		await Assert.That(result).Contains("ms-italic");
		await Assert.That(result).Contains("Styled Text");
		await Assert.That(result).Contains("style=");
		await Assert.That(result).Contains("class=");
	}

	[Test]
	public async Task Render_HtmlFormat_NoMarkup_ReturnsPlainText()
	{
		// Arrange
		var ansiMarkup = M.Create();  // no styling
		var markupString = A.markupSingle(ansiMarkup, "Plain ANSI");

		// Act
		var result = markupString.Render("html");

		// Assert - no span when there's nothing to style
		await Assert.That(result).IsEqualTo("Plain ANSI");
		await Assert.That(result).DoesNotContain("<span");
	}

	[Test]
	public async Task Render_HtmlFormat_StrikeThrough_ReturnsSpanWithStrikeClass()
	{
		// Arrange
		var ansiMarkup = M.Create(strikeThrough: true);
		var markupString = A.markupSingle(ansiMarkup, "Strike Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("ms-strike");
	}

	[Test]
	public async Task Render_HtmlFormat_ConcatenatedMarkup_RendersEachSegment()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)));

		var redText = A.markupSingle(redMarkup, "Red");
		var blueText = A.markupSingle(blueMarkup, "Blue");
		var combined = A.concat(redText, blueText);

		// Act
		var result = combined.Render("html");

		// Assert - each segment uses inline style for its color
		await Assert.That(result).Contains("color: #ff0000");
		await Assert.That(result).Contains("color: #0000ff");
		await Assert.That(result).Contains("Red");
		await Assert.That(result).Contains("Blue");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_ModuleLevelFunction_WorksCorrectly()
	{
		// Arrange
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 165, 0)));
		var markupString = A.markupSingle(ansiMarkup, "Orange");

		// Act - use module-level render function
		var result = A.render("html", markupString);

		// Assert - color via inline style
		await Assert.That(result).Contains("color: #ffa500");
		await Assert.That(result).Contains("Orange");
	}

	[Test]
	public async Task Render_InvertedMarkup_SwapsForegroundAndBackground()
	{
		// Arrange - fg=red, bg=blue, inverted=true → swap: fg=blue, bg=red
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)),
			inverted: true);
		var markupString = A.markupSingle(ansiMarkup, "Inverted");

		// Act
		var result = markupString.Render("html");

		// Assert - original fg (red) becomes background; original bg (blue) becomes foreground
		await Assert.That(result).Contains("background-color: #ff0000");
		await Assert.That(result).Contains("color: #0000ff");
	}

	[Test]
	public async Task Render_InvertedMarkup_OnlyFg_FgBecomesBackground()
	{
		// Arrange - fg=red, no bg, inverted=true → bg=red, fg=inherited (no css color)
		// This mirrors ANSI reverse-video: the foreground color moves to the background
		// and the foreground reverts to the inherited/default color.
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			inverted: true);
		var markupString = A.markupSingle(ansiMarkup, "OnlyFgInverted");

		// Act
		var result = markupString.Render("html");

		// Assert - red moved to background; no explicit foreground color emitted
		await Assert.That(result).Contains("background-color: #ff0000");
		// The style attribute must not start with "color:" (foreground) - only background-color is present
		await Assert.That(result).DoesNotContain("style=\"color:");
	}

	[Test]
	public async Task Render_InvertedMarkup_OnlyBg_BgBecomesForeground()
	{
		// Arrange - no fg, bg=blue, inverted=true → fg=blue, bg=inherited (no css background-color)
		// This mirrors ANSI reverse-video: the background color moves to the foreground.
		var ansiMarkup = M.Create(
			background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)),
			inverted: true);
		var markupString = A.markupSingle(ansiMarkup, "OnlyBgInverted");

		// Act
		var result = markupString.Render("html");

		// Assert - blue moved to foreground; no explicit background-color emitted
		await Assert.That(result).Contains("color: #0000ff");
		await Assert.That(result).DoesNotContain("background-color:");
	}

	// --- Entity escaping tests ---

	[Test]
	public async Task Render_HtmlFormat_TextWithLessThan_IsEscaped()
	{
		// Arrange
		var markupString = A.single("a < b");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).IsEqualTo("a &lt; b");
		await Assert.That(result).DoesNotContain("<b");
	}

	[Test]
	public async Task Render_HtmlFormat_TextWithGreaterThan_IsEscaped()
	{
		// Arrange
		var markupString = A.single("a > b");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).IsEqualTo("a &gt; b");
	}

	[Test]
	public async Task Render_HtmlFormat_TextWithAmpersand_IsEscaped()
	{
		// Arrange
		var markupString = A.single("rock & roll");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).IsEqualTo("rock &amp; roll");
	}

	[Test]
	public async Task Render_HtmlFormat_TextWithQuote_IsEscaped()
	{
		// Arrange
		var markupString = A.single("say \"hello\"");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).IsEqualTo("say &quot;hello&quot;");
	}

	[Test]
	public async Task Render_HtmlFormat_EscapingInsideMarkup_TextIsEscaped()
	{
		// Arrange - entity chars inside a colored span
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "<b>not a tag</b>");

		// Act
		var result = markupString.Render("html");

		// Assert - text is escaped; color is applied via inline style
		await Assert.That(result).Contains("&lt;b&gt;not a tag&lt;/b&gt;");
		await Assert.That(result).Contains("color: #ff0000");
	}

	[Test]
	public async Task Render_AnsiFormat_TextIsNotEscaped()
	{
		// Arrange - entity chars should NOT be escaped in ANSI output
		var markupString = A.single("a < b & c");

		// Act
		var result = markupString.Render("ansi");

		// Assert - ANSI format is unchanged
		await Assert.That(result).IsEqualTo("a < b & c");
	}

	// --- CSS sheet tests ---

	[Test]
	public async Task FixedCss_ContainsAllFormattingClasses()
	{
		var css = A.fixedCss;

		await Assert.That(css).Contains(".ms-bold");
		await Assert.That(css).Contains(".ms-faint");
		await Assert.That(css).Contains(".ms-italic");
		await Assert.That(css).Contains(".ms-underline");
		await Assert.That(css).Contains(".ms-strike");
		await Assert.That(css).Contains(".ms-overline");
		await Assert.That(css).Contains(".ms-blink");
	}

	[Test]
	public async Task CssSheet_NeverContainsColorClasses()
	{
		// Colors are rendered as inline styles, so the stylesheet never has fg-*/bg-* rules.
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "Red");

		var css = A.cssSheet(markupString);

		// Assert - no color classes ever appear in the stylesheet
		await Assert.That(css).DoesNotContain(".fg-");
		await Assert.That(css).DoesNotContain(".bg-");
		await Assert.That(css).Contains(".ms-bold"); // fixed classes always included
	}

	[Test]
	public async Task CssSheet_AlwaysEqualsFixedCss()
	{
		// cssSheet always returns only the fixed formatting rules regardless of content
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var combined = A.concat(
			A.markupSingle(red, "Red"),
			A.single("plain"));

		var css = A.cssSheet(combined);

		await Assert.That(css).IsEqualTo(A.fixedCss);
	}

	// ── RenderFormat discriminated union tests ──────────────────────

	[Test]
	public async Task RenderFormat_Ansi_MatchesStringBasedRender()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "RenderFormat Ansi");

		var duResult = markupString.Render(A.RenderFormat.Ansi);
		var stringResult = markupString.Render("ansi");

		await Assert.That(duResult).IsEqualTo(stringResult);
	}

	[Test]
	public async Task RenderFormat_Html_MatchesStringBasedRender()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "RenderFormat Html");

		var duResult = markupString.Render(A.RenderFormat.Html);
		var stringResult = markupString.Render("html");

		await Assert.That(duResult).IsEqualTo(stringResult);
	}

	[Test]
	public async Task RenderFormat_PlainText_MatchesStringBasedRender()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "RenderFormat Plain");

		var duResult = markupString.Render(A.RenderFormat.PlainText);
		var stringResult = markupString.Render("plaintext");

		await Assert.That(duResult).IsEqualTo(stringResult);
	}

	[Test]
	public async Task RenderFormat_PlainText_StripsMarkup()
	{
		var ansiMarkup = M.Create(bold: true, foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 255, 0)));
		var markupString = A.markupSingle(ansiMarkup, "no formatting");

		var result = markupString.Render(A.RenderFormat.PlainText);

		await Assert.That(result).IsEqualTo("no formatting");
	}

	[Test]
	public async Task RenderFormat_Custom_AppliesEncodeAndApplyMarkup()
	{
		var ansiMarkup = M.Create(bold: true);
		var markupString = A.markupSingle(ansiMarkup, "Custom Text");

		// Custom: uppercase encode, wrap with [STYLED]...[/STYLED]
		var encodeText = FuncConvert.FromFunc<string, string>(t => t.ToUpperInvariant());
		var applyMarkup = FuncConvert.FromFunc<MarkupImplementation.Markup, string, string>(
			(_, text) => "[STYLED]" + text + "[/STYLED]");

		var format = A.RenderFormat.NewCustom(encodeText, applyMarkup);
		var result = markupString.Render(format);

		await Assert.That(result).IsEqualTo("[STYLED]CUSTOM TEXT[/STYLED]");
	}

	[Test]
	public async Task RenderFormat_Custom_NoMarkup_ReturnsEncodedText()
	{
		var markupString = A.single("plain custom");

		var encodeText = FuncConvert.FromFunc<string, string>(t => t.ToUpperInvariant());
		var applyMarkup = FuncConvert.FromFunc<MarkupImplementation.Markup, string, string>(
			(_, text) => "[WRAP]" + text + "[/WRAP]");

		var format = A.RenderFormat.NewCustom(encodeText, applyMarkup);
		var result = markupString.Render(format);

		await Assert.That(result).IsEqualTo("PLAIN CUSTOM");
	}

	[Test]
	public async Task RenderFormat_ModuleLevelFunction_WorksCorrectly()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "module level");

		var duResult = A.renderFormat(A.RenderFormat.Html, markupString);
		var stringResult = A.render("html", markupString);

		await Assert.That(duResult).IsEqualTo(stringResult);
	}

	// ── Render caching tests ──────────────────────────────────────────

	[Test]
	public async Task RenderCache_Ansi_ReturnsSameInstance()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "cache ansi test");

		var result1 = markupString.Render("ansi");
		var result2 = markupString.Render("ansi");

		await Assert.That(ReferenceEquals(result1, result2)).IsTrue();
	}

	[Test]
	public async Task RenderCache_Html_ReturnsSameInstance()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "cache html test");

		var result1 = markupString.Render("html");
		var result2 = markupString.Render("html");

		await Assert.That(ReferenceEquals(result1, result2)).IsTrue();
	}

	[Test]
	public async Task RenderCache_PlainText_ReturnsSameInstance()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "cache plain test");

		var result1 = markupString.Render("plaintext");
		var result2 = markupString.Render("plaintext");

		await Assert.That(ReferenceEquals(result1, result2)).IsTrue();
	}

	[Test]
	public async Task RenderCache_DU_ReturnsSameInstanceAsStringBased()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "cache du test");

		var duResult = markupString.Render(A.RenderFormat.Html);
		var stringResult = markupString.Render("html");

		await Assert.That(ReferenceEquals(duResult, stringResult)).IsTrue();
	}

	[Test]
	public async Task RenderCache_UnknownFormat_DefaultsToAnsiCache()
	{
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "cache unknown test");

		var unknownResult = markupString.Render("xyz");
		var ansiResult = markupString.Render("ansi");

		await Assert.That(ReferenceEquals(unknownResult, ansiResult)).IsTrue();
	}
}

