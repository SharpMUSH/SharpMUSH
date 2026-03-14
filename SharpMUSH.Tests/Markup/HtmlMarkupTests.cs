using System.Drawing;
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
		var markupString = A.markupSingle(htmlMarkup, testText);

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
		var htmlMarkup = H.Create("a", Microsoft.FSharp.Core.FSharpOption<string>.Some("href=\"https://example.com\""));

		// Act
		var markupString = A.markupSingle(htmlMarkup, testText);

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
		var htmlMarkup = H.Create("div", Microsoft.FSharp.Core.FSharpOption<string>.Some("class=\"container\""));

		// Act
		var markupString = A.markupSingle(htmlMarkup, testText);

		// Assert
		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.ToString()).IsEqualTo("<div class=\"container\">test_html_markup_div_unique</div>");
	}

	[Test]
	public async Task HtmlMarkup_SpanWithId_CreatesCorrectOutput()
	{
		// Arrange
		const string testText = "test_html_markup_span_unique";
		var htmlMarkup = H.Create("span", Microsoft.FSharp.Core.FSharpOption<string>.Some("id=\"test-id\""));

		// Act
		var markupString = A.markupSingle(htmlMarkup, testText);

		// Assert
		await Assert.That(markupString.ToString()).IsEqualTo("<span id=\"test-id\">test_html_markup_span_unique</span>");
	}

	[Test]
	public async Task HtmlMarkup_ConcatenateMultipleTags_WorksCorrectly()
	{
		// Arrange
		var boldMarkup = H.Create("b");
		var italicMarkup = H.Create("i");

		var boldText = A.markupSingle(boldMarkup, "Bold");
		var italicText = A.markupSingle(italicMarkup, "Italic");

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
		var divMarkup = H.Create("div", Microsoft.FSharp.Core.FSharpOption<string>.Some("class=\"outer\""));
		var spanMarkup = H.Create("span", Microsoft.FSharp.Core.FSharpOption<string>.Some("class=\"inner\""));

		var innerText = A.markupSingle(spanMarkup, "Inner");
		var outerText = A.markupSingle2(divMarkup, innerText);

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
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));

		// Create HTML wrapped text
		var htmlText = A.markupSingle(htmlMarkup, testText);

		// Create ANSI colored text
		var ansiText = A.markupSingle(ansiMarkup, testText);

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
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		// Create ANSI colored text
		var coloredText = A.markupSingle(ansiMarkup, testText);

		// Wrap it in HTML
		var htmlWrapped = A.markupSingle2(htmlMarkup, coloredText);

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
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Green));

		// Create HTML tagged text
		var htmlText = A.markupSingle(htmlMarkup, testText);

		// Wrap it in ANSI color
		var ansiWrapped = A.markupSingle2(ansiMarkup, htmlText);

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
		var divHtml = H.Create("div", Microsoft.FSharp.Core.FSharpOption<string>.Some("class=\"test\""));
		var redAnsi = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueAnsi = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		// Create complex nested structure
		var text1 = A.markupSingle(boldHtml, "Bold");
		var text2 = A.markupSingle(redAnsi, "Red");
		var text3 = A.markupSingle(blueAnsi, "Blue");
		var combined = A.concat(A.concat(text1, text2), text3);
		var wrapped = A.markupSingle2(divHtml, combined);

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
		var original = A.markupSingle(htmlMarkup, "Test HTML");

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
		var htmlMarkup = H.Create("a", Microsoft.FSharp.Core.FSharpOption<string>.Some("href=\"https://test.com\""));
		var original = A.markupSingle(htmlMarkup, "test_html_serialize_attrs");

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
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Yellow));

		var ansiText = A.markupSingle(ansiMarkup, "test_mixed_serialize");
		var htmlWrapped = A.markupSingle2(htmlMarkup, ansiText);

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
		var markupString = A.markupSingle(htmlMarkup, "Hello, World!");

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
		var markupString = A.markupSingle(htmlMarkup, "one,two,three");

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
		var first = A.markupSingle(boldMarkup, "Hello ");
		var second = A.markupSingle(boldMarkup, "World");
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
		var first = A.markupSingle(boldMarkup, "Bold ");
		var second = A.markupSingle(italicMarkup, "Italic");
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
		var htmlMarkup = H.Create("span", Microsoft.FSharp.Core.FSharpOption<string>.Some("class=\"item\""));
		var markupStrings = new[]
		{
			A.markupSingle(htmlMarkup, "one"),
			A.markupSingle(htmlMarkup, "two"),
			A.markupSingle(htmlMarkup, "three")
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
		var markupString = A.markupSingle(htmlMarkup, "");

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
		var result = A.markupSingle2(htmlMarkup, content);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("content");
		await Assert.That(result.ToString()).IsEqualTo("<b>content</b>");
	}

	[Test]
	public async Task HtmlMarkup_TagwrapFunctionWithAttributes_Integration()
	{
		// Arrange - simulating tagwrap(div, content, class="test")
		var htmlMarkup = H.Create("div", Microsoft.FSharp.Core.FSharpOption<string>.Some("class=\"test\""));
		var content = A.single("test_tagwrap_attrs_unique");

		// Act
		var result = A.markupSingle2(htmlMarkup, content);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("test_tagwrap_attrs_unique");
		await Assert.That(result.ToString()).IsEqualTo("<div class=\"test\">test_tagwrap_attrs_unique</div>");
	}
}
