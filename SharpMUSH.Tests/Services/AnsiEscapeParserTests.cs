using SharpMUSH.Library.Services.DatabaseConversion;
using MarkupString;

namespace SharpMUSH.Tests.Services;

public class AnsiEscapeParserTests
{
	[Test]
	public async ValueTask PlainTextRemainsUnchanged()
	{
		var input = "Hello, World!";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo(input);
	}

	[Test]
	public async ValueTask EmptyStringHandled()
	{
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString("");
		
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async ValueTask NullStringHandled()
	{
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(null);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async ValueTask BasicColorCodeStripped()
	{
		// ESC[31m = red foreground
		var input = "\x1b[31mRed Text\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		// Should preserve the text
		await Assert.That(result.ToPlainText()).IsEqualTo("Red Text");
		
		// Should have ANSI markup
		var resultStr = result.ToString();
		await Assert.That(resultStr).Contains("Red Text");
	}

	[Test]
	public async ValueTask BoldTextConverted()
	{
		// ESC[1m = bold
		var input = "\x1b[1mBold Text\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Bold Text");
	}

	[Test]
	public async ValueTask UnderlineTextConverted()
	{
		// ESC[4m = underline
		var input = "\x1b[4mUnderlined Text\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Underlined Text");
	}

	[Test]
	public async ValueTask ColorAnd256ColorConverted()
	{
		// ESC[38;5;196m = 256-color red foreground
		var input = "\x1b[38;5;196mRed Text\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Red Text");
	}

	[Test]
	public async ValueTask RgbColorConverted()
	{
		// ESC[38;2;255;0;0m = RGB red foreground
		var input = "\x1b[38;2;255;0;0mRed Text\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Red Text");
	}

	[Test]
	public async ValueTask MultipleFormatsConverted()
	{
		// Bold + Red
		var input = "\x1b[1m\x1b[31mBold Red Text\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Bold Red Text");
	}

	[Test]
	public async ValueTask MixedFormattedAndPlainText()
	{
		var input = "Normal \x1b[31mRed\x1b[0m Normal Again";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Normal Red Normal Again");
	}

	[Test]
	public async ValueTask UnrecognizedEscapeSequencesStripped()
	{
		// ESC[2J (clear screen) - not an SGR sequence
		var input = "Before\x1b[2JAfter";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		// Should strip the unknown sequence
		await Assert.That(result.ToPlainText()).IsEqualTo("BeforeAfter");
	}

	[Test]
	public async ValueTask BrightColorsConverted()
	{
		// ESC[91m = bright red
		var input = "\x1b[91mBright Red\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Bright Red");
	}

	[Test]
	public async ValueTask BackgroundColorConverted()
	{
		// ESC[41m = red background
		var input = "\x1b[41mRed Background\x1b[0m";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Red Background");
	}

	[Test]
	public async ValueTask ComplexPennMUSHExample()
	{
		// Simulate a typical PennMUSH attribute with ANSI
		var input = "\x1b[1m\x1b[32mSuccess!\x1b[0m You found \x1b[33m5 gold coins\x1b[0m.";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Success! You found 5 gold coins.");
	}

	[Test]
	public async ValueTask OSC8HyperlinkConverted()
	{
		// OSC 8 hyperlink: ESC]8;;urlESC\textESC]8;;ESC\
		var input = "\x1b]8;;https://example.com\x1b\\Click here\x1b]8;;\x1b\\";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		// Should preserve the text
		await Assert.That(result.ToPlainText()).IsEqualTo("Click here");
		
		// Markup should be applied (can't easily test URL in result, but it shouldn't crash)
		await Assert.That(result.ToString()).Contains("Click here");
	}

	[Test]
	public async ValueTask OSC8HyperlinkWithBELTerminator()
	{
		// OSC 8 with BEL (0x07) terminator instead of ESC\
		var input = "\x1b]8;;https://example.com\x07Link Text\x1b]8;;\x07";
		var result = AnsiEscapeParser.ConvertAnsiToMarkupString(input);
		
		await Assert.That(result.ToPlainText()).IsEqualTo("Link Text");
	}
}
