using System.Drawing;
using MarkupString;
using AMS = MarkupString.MarkupStringModule;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using H = MarkupString.MarkupImplementation.HtmlMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Unit tests for the Strategy Pattern render pipeline (section 5.2 Option A).
/// Tests IRenderStrategy interface, built-in strategies (ANSI, HTML, PlainText),
/// the RenderStrategies registry, RenderWith method, and custom strategy extensibility.
/// </summary>
public class RenderStrategyTests
{
	// ── AnsiRenderStrategy ────────────────────────────────────────

	[Test]
	public async Task AnsiStrategy_PlainText_PassesThrough()
	{
		var ams = AMS.single("Hello");
		var result = ams.RenderWith(AMS.RenderStrategies.ansi);

		await Assert.That(result).IsEqualTo("Hello");
	}

	[Test]
	public async Task AnsiStrategy_WithMarkup_ContainsEscapeCodes()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var result = ams.RenderWith(AMS.RenderStrategies.ansi);

		await Assert.That(result).Contains("\u001b[");
		await Assert.That(result).Contains("Red Text");
	}

	[Test]
	public async Task AnsiStrategy_MatchesToString()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var strategyResult = ams.RenderWith(AMS.RenderStrategies.ansi);
		var toStringResult = ams.ToString();

		await Assert.That(strategyResult).IsEqualTo(toStringResult);
	}

	[Test]
	public async Task AnsiStrategy_MatchesRenderAnsi()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var strategyResult = ams.RenderWith(AMS.RenderStrategies.ansi);
		var renderResult = ams.Render("ansi");

		await Assert.That(strategyResult).IsEqualTo(renderResult);
	}

	[Test]
	public async Task AnsiStrategy_BoldMarkup_ContainsBoldEscape()
	{
		var boldMarkup = M.Create(bold: true);
		var ams = AMS.markupSingle(boldMarkup, "Bold");

		var result = ams.RenderWith(AMS.RenderStrategies.ansi);

		await Assert.That(result).Contains("\u001b[");
		await Assert.That(result).Contains("Bold");
	}

	[Test]
	public async Task AnsiStrategy_EmptyString_ReturnsEmpty()
	{
		var ams = AMS.empty();
		var result = ams.RenderWith(AMS.RenderStrategies.ansi);

		await Assert.That(result).IsEqualTo("");
	}

	[Test]
	public async Task AnsiStrategy_ConcatenatedMarkup_OptimizesOutput()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var part1 = AMS.markupSingle(redMarkup, "Hello");
		var part2 = AMS.markupSingle(redMarkup, " World");
		var combined = AMS.concat(part1, part2);

		// Optimized version should merge redundant escape sequences
		var optimized = AMS.optimize(combined);
		var result = optimized.RenderWith(AMS.RenderStrategies.ansi);

		await Assert.That(result).Contains("Hello World");
		await Assert.That(result).Contains("\u001b[");
	}

	// ── HtmlRenderStrategy ────────────────────────────────────────

	[Test]
	public async Task HtmlStrategy_PlainText_PassesThrough()
	{
		var ams = AMS.single("Hello");
		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).IsEqualTo("Hello");
	}

	[Test]
	public async Task HtmlStrategy_WithAnsiMarkup_ContainsSpanTags()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).Contains("<span");
		await Assert.That(result).Contains("color: #ff0000");
		await Assert.That(result).Contains("Red Text");
		await Assert.That(result).Contains("</span>");
	}

	[Test]
	public async Task HtmlStrategy_MatchesRenderHtml()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var strategyResult = ams.RenderWith(AMS.RenderStrategies.html);
		var renderResult = ams.Render("html");

		await Assert.That(strategyResult).IsEqualTo(renderResult);
	}

	[Test]
	public async Task HtmlStrategy_HtmlEncodesText()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "<script>alert('xss')</script>");

		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).DoesNotContain("<script>");
		await Assert.That(result).Contains("&lt;script&gt;");
	}

	[Test]
	public async Task HtmlStrategy_BoldMarkup_ContainsCssClass()
	{
		var boldMarkup = M.Create(bold: true);
		var ams = AMS.markupSingle(boldMarkup, "Bold");

		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).Contains("ms-bold");
	}

	[Test]
	public async Task HtmlStrategy_HtmlMarkup_RendersHtmlTags()
	{
		var htmlMarkup = H.Create("b");
		var ams = AMS.markupSingle(htmlMarkup, "Bold");

		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).IsEqualTo("<b>Bold</b>");
	}

	[Test]
	public async Task HtmlStrategy_NoEscapeCodes()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Text");

		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task HtmlStrategy_BackgroundColor_ContainsStyleAttribute()
	{
		var markup = M.Create(background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 128, 0)));
		var ams = AMS.markupSingle(markup, "Green BG");

		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).Contains("background-color: #008000");
	}

	[Test]
	public async Task HtmlStrategy_EmptyString_ReturnsEmpty()
	{
		var ams = AMS.empty();
		var result = ams.RenderWith(AMS.RenderStrategies.html);

		await Assert.That(result).IsEqualTo("");
	}

	// ── PlainTextRenderStrategy ───────────────────────────────────

	[Test]
	public async Task PlainTextStrategy_PlainText_PassesThrough()
	{
		var ams = AMS.single("Hello");
		var result = ams.RenderWith(AMS.RenderStrategies.plainText);

		await Assert.That(result).IsEqualTo("Hello");
	}

	[Test]
	public async Task PlainTextStrategy_WithMarkup_StripsFormatting()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var result = ams.RenderWith(AMS.RenderStrategies.plainText);

		await Assert.That(result).IsEqualTo("Red Text");
		await Assert.That(result).DoesNotContain("\u001b[");
		await Assert.That(result).DoesNotContain("<span");
	}

	[Test]
	public async Task PlainTextStrategy_MatchesToPlainText()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var strategyResult = ams.RenderWith(AMS.RenderStrategies.plainText);
		var plainResult = ams.ToPlainText();

		await Assert.That(strategyResult).IsEqualTo(plainResult);
	}

	[Test]
	public async Task PlainTextStrategy_ConcatenatedMarkup_StripsAll()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var part1 = AMS.markupSingle(redMarkup, "Hello");
		var part2 = AMS.markupSingle(blueMarkup, " World");
		var combined = AMS.concat(part1, part2);

		var result = combined.RenderWith(AMS.RenderStrategies.plainText);

		await Assert.That(result).IsEqualTo("Hello World");
	}

	[Test]
	public async Task PlainTextStrategy_EmptyString_ReturnsEmpty()
	{
		var ams = AMS.empty();
		var result = ams.RenderWith(AMS.RenderStrategies.plainText);

		await Assert.That(result).IsEqualTo("");
	}

	// ── forFormat function ────────────────────────

	[Test]
	public async Task ForFormat_Ansi_ReturnsAnsiStrategy()
	{
		var strategy = AMS.forFormat(AMS.RenderFormat.Ansi);
		var ams = AMS.single("Test");
		var result = ams.RenderWith(strategy);

		await Assert.That(result).IsEqualTo("Test");
	}

	[Test]
	public async Task ForFormat_Html_ReturnsHtmlStrategy()
	{
		var strategy = AMS.forFormat(AMS.RenderFormat.Html);
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var ams = AMS.markupSingle(redMarkup, "Test");
		var result = ams.RenderWith(strategy);

		await Assert.That(result).Contains("<span");
	}

	[Test]
	public async Task ForFormat_PlainText_ReturnsPlainTextStrategy()
	{
		var strategy = AMS.forFormat(AMS.RenderFormat.PlainText);
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Test");
		var result = ams.RenderWith(strategy);

		await Assert.That(result).IsEqualTo("Test");
	}

	[Test]
	public async Task ForFormat_MatchesToStrategy()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Test");

		var forFormatResult = ams.RenderWith(AMS.forFormat(AMS.RenderFormat.Ansi));
		var toStrategyResult = ams.RenderWith(AMS.RenderFormat.Ansi.ToStrategy());

		await Assert.That(forFormatResult).IsEqualTo(toStrategyResult);
	}

	// ── RenderWith method ─────────────────────────────────────────

	[Test]
	public async Task RenderWith_AcceptsDifferentStrategies()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Test");

		var ansiResult = ams.RenderWith(AMS.RenderStrategies.ansi);
		var htmlResult = ams.RenderWith(AMS.RenderStrategies.html);
		var plainResult = ams.RenderWith(AMS.RenderStrategies.plainText);

		// All three should produce different outputs for marked-up text
		await Assert.That(ansiResult).IsNotEqualTo(htmlResult);
		await Assert.That(ansiResult).IsNotEqualTo(plainResult);
		await Assert.That(htmlResult).IsNotEqualTo(plainResult);
	}

	[Test]
	public async Task RenderWith_MultipleRuns_AllStrategiesWork()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var plain = AMS.single("Hello ");
		var red = AMS.markupSingle(redMarkup, "Red ");
		var blue = AMS.markupSingle(blueMarkup, "Blue");
		var combined = AMS.concat(AMS.concat(plain, red), blue);

		var ansiResult = combined.RenderWith(AMS.RenderStrategies.ansi);
		var htmlResult = combined.RenderWith(AMS.RenderStrategies.html);
		var plainResult = combined.RenderWith(AMS.RenderStrategies.plainText);

		// All should contain the plain text
		await Assert.That(plainResult).IsEqualTo("Hello Red Blue");

		// ANSI should have escape codes
		await Assert.That(ansiResult).Contains("\u001b[");
		await Assert.That(ansiResult).Contains("Hello ");
		await Assert.That(ansiResult).Contains("Red ");
		await Assert.That(ansiResult).Contains("Blue");

		// HTML should have span tags
		await Assert.That(htmlResult).Contains("<span");
		await Assert.That(htmlResult).Contains("Hello ");
	}

	// ── Custom Strategy (extensibility) ───────────────────────────

	[Test]
	public async Task CustomStrategy_BBCode_WorksWithRenderWith()
	{
		// Create a custom BBCode render strategy to demonstrate extensibility
		var bbCodeStrategy = new BBCodeRenderStrategy();

		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var boldMarkup = M.Create(bold: true);
		var ams = AMS.markupSingle(redMarkup, "Red Text");
		var amsBold = AMS.markupSingle(boldMarkup, "Bold Text");

		var redResult = ams.RenderWith(bbCodeStrategy);
		var boldResult = amsBold.RenderWith(bbCodeStrategy);

		await Assert.That(redResult).IsEqualTo("[color=#ff0000]Red Text[/color]");
		await Assert.That(boldResult).IsEqualTo("[b]Bold Text[/b]");
	}

	[Test]
	public async Task CustomStrategy_BBCode_PlainTextPassesThrough()
	{
		var bbCodeStrategy = new BBCodeRenderStrategy();
		var ams = AMS.single("No formatting");

		var result = ams.RenderWith(bbCodeStrategy);

		await Assert.That(result).IsEqualTo("No formatting");
	}

	[Test]
	public async Task CustomStrategy_BBCode_ConcatenatedRuns()
	{
		var bbCodeStrategy = new BBCodeRenderStrategy();
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var red = AMS.markupSingle(redMarkup, "Red");
		var plain = AMS.single(" and ");
		var blue = AMS.markupSingle(blueMarkup, "Blue");
		var combined = AMS.concat(AMS.concat(red, plain), blue);

		var result = combined.RenderWith(bbCodeStrategy);

		await Assert.That(result).IsEqualTo("[color=#ff0000]Red[/color] and [color=#0000ff]Blue[/color]");
	}

	[Test]
	public async Task CustomStrategy_MarkdownBold_WorksWithRenderWith()
	{
		// Another custom strategy: Markdown-style bold
		var markdownStrategy = new MarkdownBoldRenderStrategy();
		var boldMarkup = M.Create(bold: true);
		var ams = AMS.markupSingle(boldMarkup, "Important");

		var result = ams.RenderWith(markdownStrategy);

		await Assert.That(result).IsEqualTo("**Important**");
	}

	// ── Strategy singletons ───────────────────────────────────────

	[Test]
	public async Task Singletons_AreSameInstance()
	{
		var ansi1 = AMS.RenderStrategies.ansi;
		var ansi2 = AMS.RenderStrategies.ansi;
		var html1 = AMS.RenderStrategies.html;
		var html2 = AMS.RenderStrategies.html;
		var plain1 = AMS.RenderStrategies.plainText;
		var plain2 = AMS.RenderStrategies.plainText;

		await Assert.That(ReferenceEquals(ansi1, ansi2)).IsTrue();
		await Assert.That(ReferenceEquals(html1, html2)).IsTrue();
		await Assert.That(ReferenceEquals(plain1, plain2)).IsTrue();
	}

	// ── Backward compatibility ────────────────────────────────────

	[Test]
	public async Task BackwardCompat_RenderAnsi_SameAsRenderWithAnsi()
	{
		var markup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red), bold: true);
		var ams = AMS.markupSingle(markup, "Test");

		await Assert.That(ams.Render("ansi")).IsEqualTo(ams.RenderWith(AMS.RenderStrategies.ansi));
	}

	[Test]
	public async Task BackwardCompat_RenderHtml_SameAsRenderWithHtml()
	{
		var markup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)), bold: true);
		var ams = AMS.markupSingle(markup, "Test");

		await Assert.That(ams.Render("html")).IsEqualTo(ams.RenderWith(AMS.RenderStrategies.html));
	}

	[Test]
	public async Task BackwardCompat_RenderPlainText_SameAsRenderWithPlainText()
	{
		var markup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red), bold: true);
		var ams = AMS.markupSingle(markup, "Test");

		await Assert.That(ams.Render("plaintext")).IsEqualTo(ams.RenderWith(AMS.RenderStrategies.plainText));
	}
}

/// <summary>
/// Custom BBCode render strategy demonstrating extensibility.
/// Converts ANSI markup to BBCode format: [color=#rrggbb]text[/color], [b]text[/b], etc.
/// </summary>
internal class BBCodeRenderStrategy : AMS.IRenderStrategy
{
	public string EncodeText(string text) => text;

	public string ApplyMarkup(MarkupImplementation.Markup markup, string text)
	{
		if (markup is M ansiMarkup)
		{
			var result = text;
			var details = ansiMarkup.Details;

			// Apply bold
			if (details.Bold)
				result = $"[b]{result}[/b]";

			// Apply italic
			if (details.Italic)
				result = $"[i]{result}[/i]";

			// Apply underline
			if (details.Underlined)
				result = $"[u]{result}[/u]";

			// Apply strikethrough
			if (details.StrikeThrough)
				result = $"[s]{result}[/s]";

			// Apply foreground color
			if (details.Foreground is ANSILibrary.ANSI.AnsiColor.RGB rgb)
				result = $"[color=#{rgb.Item.R:x2}{rgb.Item.G:x2}{rgb.Item.B:x2}]{result}[/color]";

			return result;
		}

		if (markup is H htmlMarkup)
		{
			return $"[{htmlMarkup.Details.TagName}]{text}[/{htmlMarkup.Details.TagName}]";
		}

		return text;
	}

	public string Prefix => "";
	public string Postfix => "";
	public string Optimize(string text) => text;
}

/// <summary>
/// Custom Markdown render strategy demonstrating extensibility.
/// Converts ANSI markup to Markdown format: **bold**, *italic*, etc.
/// </summary>
internal class MarkdownBoldRenderStrategy : AMS.IRenderStrategy
{
	public string EncodeText(string text) => text;

	public string ApplyMarkup(MarkupImplementation.Markup markup, string text)
	{
		if (markup is M ansiMarkup)
		{
			var result = text;
			var details = ansiMarkup.Details;

			if (details.Bold)
				result = $"**{result}**";

			if (details.Italic)
				result = $"*{result}*";

			if (details.StrikeThrough)
				result = $"~~{result}~~";

			return result;
		}

		return text;
	}

	public string Prefix => "";
	public string Postfix => "";
	public string Optimize(string text) => text;
}
