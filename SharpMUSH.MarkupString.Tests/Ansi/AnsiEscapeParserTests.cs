using MarkupString.Ansi;

public class AnsiEscapeParserTests
{
	private const string E = "\u001b";

	private static AnsiStyle StyleOf(MarkupText text, int runIndex = 0) =>
		((AnsiMarkup)text.Runs[runIndex].Markups.Innermost).Style;

	[Test]
	public async Task PlainText_RemainsUnchanged()
	{
		var result = AnsiEscapeParser.Parse("Hello, World!");
		await Assert.That(result.Text).IsEqualTo("Hello, World!");
		await Assert.That(result.Runs.Length).IsEqualTo(0);
	}

	[Test]
	public async Task EmptyString_IsEmpty()
	{
		await Assert.That(AnsiEscapeParser.Parse("").Text).IsEqualTo("");
	}

	[Test]
	public async Task NullString_IsEmpty()
	{
		await Assert.That(AnsiEscapeParser.Parse(null).Text).IsEqualTo("");
	}

	[Test]
	public async Task BasicColourCode_BecomesAStandardForeground()
	{
		var result = AnsiEscapeParser.Parse($"{E}[31mRed Text{E}[0m");
		await Assert.That(result.Text).IsEqualTo("Red Text");
		await Assert.That(StyleOf(result).Foreground).IsEqualTo(new AnsiColor.Standard(1, false));
	}

	[Test]
	public async Task BoldCode_SetsBold()
	{
		var result = AnsiEscapeParser.Parse($"{E}[1mBold Text{E}[0m");
		await Assert.That(result.Text).IsEqualTo("Bold Text");
		await Assert.That(StyleOf(result).Bold).IsTrue();
	}

	[Test]
	public async Task UnderlineCode_SetsUnderlined()
	{
		var result = AnsiEscapeParser.Parse($"{E}[4mUnderlined{E}[0m");
		await Assert.That(StyleOf(result).Underlined).IsTrue();
	}

	[Test]
	public async Task XtermCode_BecomesAnXtermColour()
	{
		var result = AnsiEscapeParser.Parse($"{E}[38;5;200mx");
		await Assert.That(result.Text).IsEqualTo("x");
		await Assert.That(StyleOf(result).Foreground).IsEqualTo(new AnsiColor.Xterm(200));
	}

	[Test]
	public async Task XtermBackgroundCode_BecomesAnXtermBackground()
	{
		var result = AnsiEscapeParser.Parse($"{E}[48;5;17mx");
		await Assert.That(StyleOf(result).Background).IsEqualTo(new AnsiColor.Xterm(17));
	}

	[Test]
	public async Task RgbCode_BecomesAnRgbColour()
	{
		var result = AnsiEscapeParser.Parse($"{E}[38;2;255;0;0mRed Text{E}[0m");
		await Assert.That(result.Text).IsEqualTo("Red Text");
		await Assert.That(StyleOf(result).Foreground).IsEqualTo(new AnsiColor.Rgb(255, 0, 0));
	}

	[Test]
	public async Task RgbBackgroundCode_BecomesAnRgbBackground()
	{
		var result = AnsiEscapeParser.Parse($"{E}[48;2;1;2;3mx");
		await Assert.That(StyleOf(result).Background).IsEqualTo(new AnsiColor.Rgb(1, 2, 3));
	}

	[Test]
	public async Task BoldAndColour_AreBothKeptAndTheColourStaysDim()
	{
		var result = AnsiEscapeParser.Parse($"{E}[1;34mx");
		var style = StyleOf(result);
		await Assert.That(style.Bold).IsTrue();
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Standard(4, false));
	}

	[Test]
	public async Task SeparateBoldAndColourSequences_Accumulate()
	{
		var result = AnsiEscapeParser.Parse($"{E}[1m{E}[31mBold Red Text{E}[0m");
		var style = StyleOf(result);
		await Assert.That(result.Text).IsEqualTo("Bold Red Text");
		await Assert.That(style.Bold).IsTrue();
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Standard(1, false));
	}

	[Test]
	public async Task MixedFormattedAndPlainText_KeepsOnlyTheStyledRun()
	{
		var result = AnsiEscapeParser.Parse($"Normal {E}[31mRed{E}[0m Normal Again");
		await Assert.That(result.Text).IsEqualTo("Normal Red Normal Again");
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.Runs[0].Start).IsEqualTo(7);
		await Assert.That(result.Runs[0].Length).IsEqualTo(3);
	}

	[Test]
	public async Task UnrecognisedEscapeSequences_AreStripped()
	{
		var result = AnsiEscapeParser.Parse($"Before{E}[2JAfter");
		await Assert.That(result.Text).IsEqualTo("BeforeAfter");
		await Assert.That(result.Runs.Length).IsEqualTo(0);
	}

	[Test]
	public async Task BrightColourCodes_AreBrightStandardColours()
	{
		var result = AnsiEscapeParser.Parse($"{E}[91mBright Red{E}[0m");
		await Assert.That(result.Text).IsEqualTo("Bright Red");
		await Assert.That(StyleOf(result).Foreground).IsEqualTo(new AnsiColor.Standard(1, true));
	}

	[Test]
	public async Task BrightBackgroundCodes_AreBrightStandardBackgrounds()
	{
		var result = AnsiEscapeParser.Parse($"{E}[104mx");
		await Assert.That(StyleOf(result).Background).IsEqualTo(new AnsiColor.Standard(4, true));
	}

	[Test]
	public async Task BackgroundColourCode_BecomesAStandardBackground()
	{
		var result = AnsiEscapeParser.Parse($"{E}[41mRed Background{E}[0m");
		await Assert.That(result.Text).IsEqualTo("Red Background");
		await Assert.That(StyleOf(result).Background).IsEqualTo(new AnsiColor.Standard(1, false));
	}

	[Test]
	public async Task DefaultColourCodes_BecomeTheDefaultColour()
	{
		var result = AnsiEscapeParser.Parse($"{E}[39;49mx");
		var style = StyleOf(result);
		await Assert.That(style.Foreground).IsEqualTo(AnsiColor.Default.Instance);
		await Assert.That(style.Background).IsEqualTo(AnsiColor.Default.Instance);
	}

	[Test]
	[Arguments(22, "Bold")]
	[Arguments(23, "Italic")]
	[Arguments(24, "Underlined")]
	[Arguments(25, "Blink")]
	[Arguments(27, "Inverted")]
	[Arguments(29, "StrikeThrough")]
	public async Task TurnOffCodes_ClearTheirAttribute(int off, string attribute)
	{
		var on = off switch
		{
			22 => 1,
			23 => 3,
			24 => 4,
			25 => 5,
			27 => 7,
			_ => 9
		};
		// The colour keeps a run alive, so the turned-off attribute is checked on a style that still
		// exists rather than on the absence of markup.
		var result = AnsiEscapeParser.Parse($"{E}[31m{E}[{on}m{E}[{off}mx");
		var style = StyleOf(result);
		var value = attribute switch
		{
			"Bold" => style.Bold,
			"Italic" => style.Italic,
			"Underlined" => style.Underlined,
			"Blink" => style.Blink,
			"Inverted" => style.Inverted,
			_ => style.StrikeThrough
		};
		await Assert.That(value).IsFalse();
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Standard(1, false));
		await Assert.That(result.Text).IsEqualTo("x");
	}

	[Test]
	public async Task ResetCode_DropsEveryAttribute()
	{
		var result = AnsiEscapeParser.Parse($"{E}[1;31ma{E}[0mb");
		await Assert.That(result.Text).IsEqualTo("ab");
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.Runs[0].Length).IsEqualTo(1);
	}

	[Test]
	public async Task ComplexPennMushExample_KeepsEveryCharacter()
	{
		var result = AnsiEscapeParser.Parse($"{E}[1m{E}[32mSuccess!{E}[0m You found {E}[33m5 gold coins{E}[0m.");
		await Assert.That(result.Text).IsEqualTo("Success! You found 5 gold coins.");
		await Assert.That(result.Runs.Length).IsEqualTo(2);
	}

	[Test]
	public async Task Osc8Hyperlink_WithStringTerminator_BecomesAUrlLink()
	{
		var result = AnsiEscapeParser.Parse($"{E}]8;;https://example.com{E}\\Click here{E}]8;;{E}\\");
		await Assert.That(result.Text).IsEqualTo("Click here");
		var style = StyleOf(result);
		await Assert.That(style.LinkUrl).IsEqualTo("https://example.com");
		await Assert.That(style.LinkKind).IsEqualTo(LinkKind.Url);
	}

	[Test]
	public async Task Osc8Hyperlink_WithBellTerminator_BecomesAUrlLink()
	{
		var result = AnsiEscapeParser.Parse($"{E}]8;;https://example.com\u0007Link Text{E}]8;;\u0007");
		await Assert.That(result.Text).IsEqualTo("Link Text");
		await Assert.That(StyleOf(result).LinkUrl).IsEqualTo("https://example.com");
	}

	[Test]
	public async Task Osc8Hyperlink_RoundTripsThroughTheAnsiRenderer()
	{
		var registry = MarkupRegistry.Empty.WithAnsi();
		var result = AnsiEscapeParser.Parse($"{E}]8;;http://x\u0007a{E}]8;;\u0007");
		await Assert.That(result.Render(MarkupFormat.Ansi, registry))
			.IsEqualTo($"{E}]8;;http://x\u0007a{E}]8;;\u0007");
	}

	[Test]
	public async Task TrailingEscape_IsDropped()
	{
		await Assert.That(AnsiEscapeParser.Parse($"abc{E}").Text).IsEqualTo("abc");
	}

	[Test]
	public async Task UnterminatedCsi_ConsumesTheRest()
	{
		await Assert.That(AnsiEscapeParser.Parse($"abc{E}[31").Text).IsEqualTo("abc");
	}
}
