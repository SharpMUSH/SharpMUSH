using System.Drawing;
using ANSILibrary;
using A = MarkupString.MarkupStringModule;
using H = MarkupString.MarkupImplementation.HtmlMarkup;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Unit tests for HtmlMarkup functionality and integration with AnsiMarkup.
/// </summary>
public class HtmlMarkupTests
{
	[Test]
	public async Task HtmlMarkup_SimpleTag_CreatesCorrectOutput()
	{
		// Arrange
		const string testText = "Bold Text";
		var htmlMarkup = H.Create("b");

		// Act
		var markupString = A.MarkupSingle(htmlMarkup, testText);

		// Assert
		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.Length).IsEqualTo(testText.Length);

		// The toString should contain HTML tags
		var stringRepresentation = markupString.ToString();
		await Assert.That(stringRepresentation).IsEqualTo("<b>Bold Text</b>");
	}

	[Test]
	public async Task HtmlMarkup_TagWithAttributes_CreatesCorrectOutput()
	{
		// Arrange
		const string testText = "Link Text";
		var htmlMarkup = H.Create("a", "href=\"https://example.com\"");

		// Act
		var markupString = A.MarkupSingle(htmlMarkup, testText);

		// Assert
		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.Length).IsEqualTo(testText.Length);

		// The toString should contain HTML tags with attributes
		var stringRepresentation = markupString.ToString();
		await Assert.That(stringRepresentation).IsEqualTo("<a href=\"https://example.com\">Link Text</a>");
	}

	[Test]
	public async Task HtmlMarkup_DivWithClass_CreatesCorrectOutput()
	{
		// Arrange
		const string testText = "test_html_markup_div_unique";
		var htmlMarkup = H.Create("div", "class=\"container\"");

		// Act
		var markupString = A.MarkupSingle(htmlMarkup, testText);

		// Assert
		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.ToString()).IsEqualTo("<div class=\"container\">test_html_markup_div_unique</div>");
	}

	[Test]
	public async Task HtmlMarkup_SpanWithId_CreatesCorrectOutput()
	{
		// Arrange
		const string testText = "test_html_markup_span_unique";
		var htmlMarkup = H.Create("span", "id=\"test-id\"");

		// Act
		var markupString = A.MarkupSingle(htmlMarkup, testText);

		// Assert
		await Assert.That(markupString.ToString()).IsEqualTo("<span id=\"test-id\">test_html_markup_span_unique</span>");
	}

	[Test]
	public async Task HtmlMarkup_ConcatenateMultipleTags_WorksCorrectly()
	{
		// Arrange
		var boldMarkup = H.Create("b");
		var italicMarkup = H.Create("i");

		var boldText = A.MarkupSingle(boldMarkup, "Bold");
		var italicText = A.MarkupSingle(italicMarkup, "Italic");

		// Act
		var result = A.concat(boldText, italicText);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("BoldItalic");
		await Assert.That(result.Length).IsEqualTo(10);

		var stringOutput = result.ToString();
		await Assert.That(stringOutput).Contains("<b>Bold</b>");
		await Assert.That(stringOutput).Contains("<i>Italic</i>");
	}

	[Test]
	public async Task HtmlMarkup_NestedHtmlTags_WorksCorrectly()
	{
		// Arrange
		var divMarkup = H.Create("div", "class=\"outer\"");
		var spanMarkup = H.Create("span", "class=\"inner\"");

		var innerText = A.MarkupSingle(spanMarkup, "Inner");
		var outerText = A.MarkupSingle2(divMarkup, innerText);

		// Act & Assert
		await Assert.That(outerText.ToPlainText()).IsEqualTo("Inner");
		await Assert.That(outerText.ToString()).Contains("<div class=\"outer\">");
		await Assert.That(outerText.ToString()).Contains("<span class=\"inner\">Inner</span>");
		await Assert.That(outerText.ToString()).Contains("</div>");
	}

	[Test]
	public async Task HtmlMarkup_CombinedWithAnsiMarkup_WorksTogether()
	{
		// Arrange
		const string testText = "test_combined_html_ansi_unique";
		var htmlMarkup = H.Create("b");
		var ansiMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));

		// Create HTML wrapped text
		var htmlText = A.MarkupSingle(htmlMarkup, testText);

		// Create ANSI colored text
		var ansiText = A.MarkupSingle(ansiMarkup, testText);

		// Act - concatenate them
		var result = A.concat(htmlText, ansiText);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo(testText + testText);
		await Assert.That(result.Length).IsEqualTo(testText.Length * 2);

		// The string output should contain both HTML tags and ANSI codes
		var stringOutput = result.ToString();
		await Assert.That(stringOutput).Contains("<b>");
		await Assert.That(stringOutput).Contains("</b>");
		// ANSI codes should also be present (but they're control characters)
		await Assert.That(stringOutput.Length).IsGreaterThan(testText.Length * 2);
	}

	[Test]
	public async Task HtmlMarkup_AnsiInsideHtml_WorksCorrectly()
	{
		// Arrange
		const string testText = "test_ansi_inside_html_unique";
		var htmlMarkup = H.Create("div");
		var ansiMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Blue));

		// Create ANSI colored text
		var coloredText = A.MarkupSingle(ansiMarkup, testText);

		// Wrap it in HTML
		var htmlWrapped = A.MarkupSingle2(htmlMarkup, coloredText);

		// Act & Assert
		await Assert.That(htmlWrapped.ToPlainText()).IsEqualTo(testText);

		var stringOutput = htmlWrapped.ToString();
		await Assert.That(stringOutput).Contains("<div>");
		await Assert.That(stringOutput).Contains("</div>");
		await Assert.That(stringOutput).Contains(testText);
	}

	[Test]
	public async Task HtmlMarkup_HtmlInsideAnsi_WorksCorrectly()
	{
		// Arrange
		const string testText = "test_html_inside_ansi_unique";
		var htmlMarkup = H.Create("span");
		var ansiMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Green));

		// Create HTML tagged text
		var htmlText = A.MarkupSingle(htmlMarkup, testText);

		// Wrap it in ANSI color
		var ansiWrapped = A.MarkupSingle2(ansiMarkup, htmlText);

		// Act & Assert
		await Assert.That(ansiWrapped.ToPlainText()).IsEqualTo(testText);

		var stringOutput = ansiWrapped.ToString();
		await Assert.That(stringOutput).Contains("<span>");
		await Assert.That(stringOutput).Contains("</span>");
		await Assert.That(stringOutput).Contains(testText);
	}

	[Test]
	public async Task HtmlMarkup_ComplexMixedContent_WorksCorrectly()
	{
		// Arrange
		var boldHtml = H.Create("b");
		var divHtml = H.Create("div", "class=\"test\"");
		var redAnsi = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blueAnsi = M.Create(foreground: new AnsiColor.RGB(Color.Blue));

		// Create complex nested structure
		var text1 = A.MarkupSingle(boldHtml, "Bold");
		var text2 = A.MarkupSingle(redAnsi, "Red");
		var text3 = A.MarkupSingle(blueAnsi, "Blue");
		var combined = A.concat(A.concat(text1, text2), text3);
		var wrapped = A.MarkupSingle2(divHtml, combined);

		// Act & Assert
		await Assert.That(wrapped.ToPlainText()).IsEqualTo("BoldRedBlue");
		await Assert.That(wrapped.Length).IsEqualTo(11);

		var stringOutput = wrapped.ToString();
		await Assert.That(stringOutput).Contains("<div class=\"test\">");
		await Assert.That(stringOutput).Contains("<b>Bold</b>");
		await Assert.That(stringOutput).Contains("</div>");
	}

	[Test]
	public async Task HtmlMarkup_SerializeAndDeserialize_PreservesHtml()
	{
		// Arrange
		var htmlMarkup = H.Create("b");
		var original = A.MarkupSingle(htmlMarkup, "Test HTML");

		// Act
		var serialized = A.serialize(original);
		var deserialized = A.deserialize(serialized);

		// Assert
		await Assert.That(deserialized.ToPlainText()).IsEqualTo(original.ToPlainText());
		await Assert.That(deserialized.Length).IsEqualTo(original.Length);
		await Assert.That(deserialized.ToString()).IsEqualTo(original.ToString());
	}

	[Test]
	public async Task HtmlMarkup_SerializeAndDeserialize_WithAttributes_PreservesHtml()
	{
		// Arrange
		var htmlMarkup = H.Create("a", "href=\"https://test.com\"");
		var original = A.MarkupSingle(htmlMarkup, "test_html_serialize_attrs");

		// Act
		var serialized = A.serialize(original);
		var deserialized = A.deserialize(serialized);

		// Assert
		await Assert.That(deserialized.ToPlainText()).IsEqualTo(original.ToPlainText());
		await Assert.That(deserialized.ToString()).IsEqualTo(original.ToString());
		await Assert.That(deserialized.ToString()).Contains("href=\"https://test.com\"");
	}

	[Test]
	public async Task HtmlMarkup_SerializeAndDeserialize_MixedHtmlAnsi_PreservesAll()
	{
		// Arrange
		var htmlMarkup = H.Create("div");
		var ansiMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Yellow));

		var ansiText = A.MarkupSingle(ansiMarkup, "test_mixed_serialize");
		var htmlWrapped = A.MarkupSingle2(htmlMarkup, ansiText);

		// Act
		var serialized = A.serialize(htmlWrapped);
		var deserialized = A.deserialize(serialized);

		// Assert
		await Assert.That(deserialized.ToPlainText()).IsEqualTo(htmlWrapped.ToPlainText());
		await Assert.That(deserialized.ToString()).IsEqualTo(htmlWrapped.ToString());
	}

	[Test]
	public async Task HtmlMarkup_SubstringWithHtml_PreservesMarkup()
	{
		// Arrange
		var htmlMarkup = H.Create("b");
		var markupString = A.MarkupSingle(htmlMarkup, "Hello, World!");

		// Act
		var result = A.substring(7, 5, markupString);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("World");
		await Assert.That(result.Length).IsEqualTo(5);
		await Assert.That(result.ToString()).Contains("<b>");
		await Assert.That(result.ToString()).Contains("</b>");
	}

	[Test]
	public async Task HtmlMarkup_SplitWithHtml_PreservesMarkup()
	{
		// Arrange
		var htmlMarkup = H.Create("span");
		var markupString = A.MarkupSingle(htmlMarkup, "one,two,three");

		// Act
		var result = A.split(",", markupString);

		// Assert
		await Assert.That(result.Length).IsEqualTo(3);
		await Assert.That(result[0].ToPlainText()).IsEqualTo("one");
		await Assert.That(result[1].ToPlainText()).IsEqualTo("two");
		await Assert.That(result[2].ToPlainText()).IsEqualTo("three");

		// Each split part should preserve the HTML markup
		await Assert.That(result[0].ToString()).Contains("<span>");
		await Assert.That(result[1].ToString()).Contains("<span>");
		await Assert.That(result[2].ToString()).Contains("<span>");
	}

	[Test]
	public async Task HtmlMarkup_OptimizeAdjacentSameHtml_MergesContent()
	{
		// Arrange
		var boldMarkup = H.Create("b");
		var first = A.MarkupSingle(boldMarkup, "Hello ");
		var second = A.MarkupSingle(boldMarkup, "World");
		var combined = A.concat(first, second);

		// Act
		var optimized = A.optimize(combined);

		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(optimized.Length).IsEqualTo(11);
	}

	[Test]
	public async Task HtmlMarkup_OptimizeDifferentHtml_DoesNotMerge()
	{
		// Arrange
		var boldMarkup = H.Create("b");
		var italicMarkup = H.Create("i");
		var first = A.MarkupSingle(boldMarkup, "Bold ");
		var second = A.MarkupSingle(italicMarkup, "Italic");
		var combined = A.concat(first, second);

		// Act
		var optimized = A.optimize(combined);

		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Bold Italic");
		await Assert.That(optimized.Length).IsEqualTo(11);

		// Different HTML tags should not be merged
		var output = optimized.ToString();
		await Assert.That(output).Contains("<b>");
		await Assert.That(output).Contains("<i>");
	}

	[Test]
	public async Task HtmlMarkup_MultipleWithDelimiter_WorksWithHtml()
	{
		// Arrange
		var htmlMarkup = H.Create("span", "class=\"item\"");
		var markupStrings = new[]
		{
			A.MarkupSingle(htmlMarkup, "one"),
			A.MarkupSingle(htmlMarkup, "two"),
			A.MarkupSingle(htmlMarkup, "three")
		};
		var delimiter = A.single(", ");

		// Act
		var result = A.multipleWithDelimiter(delimiter, markupStrings);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("one, two, three");
		await Assert.That(result.Length).IsEqualTo(15);

		var output = result.ToString();
		await Assert.That(output).Contains("<span class=\"item\">one</span>");
		await Assert.That(output).Contains("<span class=\"item\">two</span>");
		await Assert.That(output).Contains("<span class=\"item\">three</span>");
	}

	[Test]
	public async Task HtmlMarkup_EmptyTag_WorksCorrectly()
	{
		// Arrange
		var htmlMarkup = H.Create("br");
		var markupString = A.MarkupSingle(htmlMarkup, "");

		// Act & Assert
		await Assert.That(markupString.ToPlainText()).IsEqualTo("");
		await Assert.That(markupString.ToString()).IsEqualTo("<br></br>");
	}

	[Test]
	public async Task HtmlMarkup_TagwrapFunction_Integration()
	{
		// Arrange - simulating tagwrap(b, content)
		var htmlMarkup = H.Create("b");
		var content = A.single("content");

		// Act - using markupSingle2 like tagwrap does
		var result = A.MarkupSingle2(htmlMarkup, content);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("content");
		await Assert.That(result.ToString()).IsEqualTo("<b>content</b>");
	}

	[Test]
	public async Task HtmlMarkup_TagwrapFunctionWithAttributes_Integration()
	{
		// Arrange - simulating tagwrap(div, content, class="test")
		var htmlMarkup = H.Create("div", "class=\"test\"");
		var content = A.single("test_tagwrap_attrs_unique");

		// Act
		var result = A.MarkupSingle2(htmlMarkup, content);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("test_tagwrap_attrs_unique");
		await Assert.That(result.ToString()).IsEqualTo("<div class=\"test\">test_tagwrap_attrs_unique</div>");
	}
}

/// <summary>
/// Tests verifying that all ANSI attribute types are rendered to correct HTML spans
/// via HtmlStrategy, and that HTML special characters in plain text are properly
/// entity-encoded to prevent XSS injection.
/// </summary>
public class MStringHtmlRenderTests
{
	// ── All ANSI attribute → span/CSS mapping ─────────────────────────────────

	[Test]
	public async Task MStringToHtml_RgbForeground_ProducesColorSpan()
	{
		var markup = M.Create(foreground: new AnsiColor.RGB(Color.FromArgb(255, 128, 0)));
		var ams = A.MarkupSingle(markup, "text");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).Contains("<span");
		await Assert.That(result).Contains("color: #ff8000");
		await Assert.That(result).Contains("text");
		await Assert.That(result).Contains("</span>");
	}

	[Test]
	public async Task MStringToHtml_RgbBackground_ProducesBackgroundColorSpan()
	{
		var markup = M.Create(background: new AnsiColor.RGB(Color.FromArgb(0, 255, 64)));
		var ams = A.MarkupSingle(markup, "bg");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).Contains("background-color: #00ff40");
		await Assert.That(result).Contains("bg");
	}

	[Test]
	public async Task MStringToHtml_Bold_ProducesBoldCssClass()
	{
		var markup = M.Create(bold: true);
		var ams = A.MarkupSingle(markup, "bold text");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).Contains("ms-bold");
		await Assert.That(result).Contains("bold text");
	}

	[Test]
	public async Task MStringToHtml_Italic_ProducesItalicCssClass()
	{
		var markup = M.Create(italic: true);
		var ams = A.MarkupSingle(markup, "italic text");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).Contains("ms-italic");
		await Assert.That(result).Contains("italic text");
	}

	[Test]
	public async Task MStringToHtml_Underline_ProducesUnderlineCssClass()
	{
		var markup = M.Create(underlined: true);
		var ams = A.MarkupSingle(markup, "underlined text");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).Contains("ms-underline");
		await Assert.That(result).Contains("underlined text");
	}

	[Test]
	public async Task MStringToHtml_StrikeThrough_ProducesStrikeCssClass()
	{
		var markup = M.Create(strikeThrough: true);
		var ams = A.MarkupSingle(markup, "struck text");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).Contains("ms-strike");
		await Assert.That(result).Contains("struck text");
	}

	[Test]
	public async Task MStringToHtml_AllAnsiCodes_ProduceCorrectSpans()
	{
		// Verify all six commonly-tested ANSI attributes produce distinct, non-empty HTML output
		var cases = new (string label, M markup, string expectedHint)[]
		{
			("bold",         M.Create(bold: true),                                         "ms-bold"),
			("italic",       M.Create(italic: true),                                       "ms-italic"),
			("underline",    M.Create(underlined: true),                                   "ms-underline"),
			("strikethrough",M.Create(strikeThrough: true),                                "ms-strike"),
			("fg-color",     M.Create(foreground: new AnsiColor.RGB(Color.Red)),            "color:"),
			("bg-color",     M.Create(background: new AnsiColor.RGB(Color.Blue)),           "background-color:"),
		};

		foreach (var (label, markup, expectedHint) in cases)
		{
			var ams = A.MarkupSingle(markup, $"test_{label}");
			var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

			// Each attribute must produce a <span> wrapping the text
			await Assert.That(result).Contains("<span").Because($"{label} should produce a span");
			await Assert.That(result).Contains($"test_{label}").Because($"{label} text must be preserved");
			// The specific CSS hint must appear somewhere in the output
			await Assert.That(result.Replace(" ", "")).Contains(expectedHint.Replace(" ", ""))
				.Because($"{label} should render {expectedHint}");
		}
	}

	[Test]
	public async Task MStringToHtml_CombinedAttributes_AllCssPresent()
	{
		// Bold + italic + foreground color in one span
		var markup = M.Create(
			foreground: new AnsiColor.RGB(Color.FromArgb(200, 100, 50)),
			bold: true,
			italic: true);
		var ams = A.MarkupSingle(markup, "combo");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).Contains("ms-bold");
		await Assert.That(result).Contains("ms-italic");
		await Assert.That(result).Contains("color: #c86432");
		await Assert.That(result).Contains("combo");
	}

	// ── XSS / HTML injection prevention ──────────────────────────────────────

	[Test]
	public async Task MStringToHtml_MaliciousInput_NoXss()
	{
		// Plain text run — no markup at all
		var ams = A.single("<script>alert('xss')</script>");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).DoesNotContain("<script>");
		await Assert.That(result).DoesNotContain("</script>");
		await Assert.That(result).Contains("&lt;script&gt;");
	}

	[Test]
	public async Task MStringToHtml_MaliciousInput_InMarkupRun_NoXss()
	{
		// Dangerous text inside an ANSI-coloured run — entity-encoding must still apply
		var markup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var ams = A.MarkupSingle(markup, "<img src=x onerror=alert(1)>");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).DoesNotContain("<img");
		await Assert.That(result).Contains("&lt;img");
	}

	[Test]
	public async Task MStringToHtml_AmpersandInText_EntityEncoded()
	{
		var ams = A.single("cats & dogs");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		// Plain text with no markup — string passes through unmodified if no WrapAs is called,
		// but HtmlStrategy.EncodeText must still HTML-encode it when text is processed.
		// Verify "&" is replaced with "&amp;" in the final output.
		await Assert.That(result).Contains("cats");
		await Assert.That(result).Contains("dogs");
		// EncodeText is only called on segments that are wrapped; plain segments go through too.
		// We assert the full chain works correctly end-to-end.
		await Assert.That(result).DoesNotContain("cats & dogs");
		await Assert.That(result).Contains("cats &amp; dogs");
	}

	[Test]
	public async Task MStringToHtml_QuotesAndAngles_EntityEncoded()
	{
		var markup = M.Create(bold: true);
		var ams = A.MarkupSingle(markup, "say \"hello\" <world>");

		var result = ams.RenderWith(A.RenderStrategies.HtmlStrategy);

		await Assert.That(result).DoesNotContain("\"hello\"");
		await Assert.That(result).DoesNotContain("<world>");
		await Assert.That(result).Contains("&quot;hello&quot;");
		await Assert.That(result).Contains("&lt;world&gt;");
	}
}
