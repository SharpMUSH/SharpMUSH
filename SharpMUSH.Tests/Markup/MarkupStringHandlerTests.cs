using System.Drawing;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Extensions;
using M = MarkupString.Ansi.AnsiMarkup;
using static MarkupString.MStringInterpolation;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Tests for the <see cref="MarkupStringHandler"/> interpolated string handler.
///
/// <see cref="MString"/> does not expose a constructor accepting a <see langword="ref struct"/>
/// handler type, so the interpolated string is routed through
/// <see cref="MStringInterpolation.Format"/>:
/// <code>
/// MString result = Format($"Hello, {bold}! Count: {42}.");
/// </code>
/// </summary>
public class MarkupStringHandlerTests
{

	[Test]
	public async Task Handler_StringHole_ProducesPlainText()
	{
		string name = "World";
		MString result = Format($"Hello, {name}!");

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, World!");
		await Assert.That(result.Length).IsEqualTo(13);
		await Assert.That(result.Runs.All(r => r.Markups.Count == 0)).IsTrue();
	}

	[Test]
	public async Task Handler_IntHole_ConvertsToString()
	{
		int count = 42;
		MString result = Format($"You have {count} messages.");

		await Assert.That(result.ToPlainText()).IsEqualTo("You have 42 messages.");
	}

	[Test]
	public async Task Handler_DoubleHoleWithFormat_AppliesFormatSpecifier()
	{
		double value = 3.14159;
		MString result = Format($"Pi is {value:F2}.");

		await Assert.That(result.ToPlainText()).IsEqualTo("Pi is 3.14.");
	}

	[Test]
	public async Task Handler_BoolHole_ConvertsToString()
	{
		bool flag = true;
		MString result = Format($"Enabled: {flag}");

		await Assert.That(result.ToPlainText()).IsEqualTo("Enabled: True");
	}

	[Test]
	public async Task Handler_MStringHole_PreservesMarkupRuns()
	{
		var redMarkup = M.Create(foreground: Color.Red.ToAnsiColor());
		MString bold = MarkupText.Wrap(redMarkup, "world");

		MString result = Format($"Hello, {bold}!");

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, world!");
		// Runs cover only styled spans now, so the plain "Hello, " and "!" around the hole are gaps.
		await Assert.That(result.Runs.Length).IsEqualTo(1);

		var markedRun = result.Runs.FirstOrDefault(r => r.Start == 7 && r.Length == 5);
		await Assert.That(markedRun.Markups.Count).IsEqualTo(1);
		await Assert.That(markedRun.Markups[0]).IsEqualTo(redMarkup);
	}

	[Test]
	public async Task Handler_MultipleMStringHoles_PreservesAllMarkups()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var blue = M.Create(foreground: Color.Blue.ToAnsiColor());

		MString redWord = MarkupText.Wrap(red, "red");
		MString blueWord = MarkupText.Wrap(blue, "blue");

		MString result = Format($"Color: {redWord} and {blueWord}.");

		await Assert.That(result.ToPlainText()).IsEqualTo("Color: red and blue.");

		var ansiOutput = result.Render(MarkupFormat.Ansi);
		await Assert.That(ansiOutput).Contains("red");
		await Assert.That(ansiOutput).Contains("blue");
		await Assert.That(ansiOutput).Contains("\u001b["); // ANSI escape present
	}

	[Test]
	public async Task Handler_MStringHole_AnsiRenderMatchesConcatMany()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		MString marked = MarkupText.Wrap(red, "world");

		MString fromHandler = Format($"Hello, {marked}!");

		MString manual = MarkupText.Concat([MarkupText.Plain("Hello, "), marked, MarkupText.Plain("!")]);

		await Assert.That(fromHandler.ToPlainText()).IsEqualTo(manual.ToPlainText());
		await Assert.That(fromHandler.Render(MarkupFormat.Ansi)).IsEqualTo(manual.Render(MarkupFormat.Ansi));
		await Assert.That(fromHandler.Render(MarkupFormat.Html)).IsEqualTo(manual.Render(MarkupFormat.Html));
	}

	[Test]
	public async Task Handler_MixedHoles_CombinesCorrectly()
	{
		var boldMarkup = M.Create(bold: true);
		MString name = MarkupText.Wrap(boldMarkup, "Alice");
		int score = 99;

		MString result = Format($"Player {name} scored {score} points.");

		await Assert.That(result.ToPlainText()).IsEqualTo("Player Alice scored 99 points.");

		var boldRun = result.Runs.FirstOrDefault(r => r.Start == 7 && r.Length == 5);
		await Assert.That(boldRun.Markups.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Handler_NullStringHole_ProducesEmptySegment()
	{
		string? nullValue = null;
		MString result = Format($"before{nullValue}after");

		await Assert.That(result.ToPlainText()).IsEqualTo("beforeafter");
	}

	[Test]
	public async Task Handler_EmptyMStringHole_SkipsEmptySegment()
	{
		MString empty = MarkupText.Empty;
		MString result = Format($"before{empty}after");

		await Assert.That(result.ToPlainText()).IsEqualTo("beforeafter");
	}

	[Test]
	public async Task Handler_ConsecutiveMStringHoles_MaintainOrder()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var blue = M.Create(foreground: Color.Blue.ToAnsiColor());
		var green = M.Create(foreground: Color.Green.ToAnsiColor());

		MString a = MarkupText.Wrap(red, "R");
		MString b = MarkupText.Wrap(blue, "G");
		MString c = MarkupText.Wrap(green, "B");

		MString result = Format($"{a}{b}{c}");

		await Assert.That(result.ToPlainText()).IsEqualTo("RGB");
		await Assert.That(result.Runs.Length).IsEqualTo(3);
		await Assert.That(result.Runs[0].Start).IsEqualTo(0);
		await Assert.That(result.Runs[1].Start).IsEqualTo(1);
		await Assert.That(result.Runs[2].Start).IsEqualTo(2);
	}

	[Test]
	public async Task Handler_RunsSortedByStart()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		MString marked = MarkupText.Wrap(red, "mid");

		MString result = Format($"before {marked} after");

		for (int i = 1; i < result.Runs.Length; i++)
			await Assert.That(result.Runs[i].Start).IsGreaterThan(result.Runs[i - 1].Start);
	}

	[Test]
	public async Task Handler_SingleHoleNoLiterals_RetainsMarkup()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		MString marked = MarkupText.Wrap(red, "only");

		MString result = Format($"{marked}");

		await Assert.That(result.ToPlainText()).IsEqualTo("only");
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.Runs[0].Markups.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Handler_Trim_DefaultTrimsBothSides()
	{
		MString value = MarkupText.Plain("  hello  ");
		MString result = Format($"{value:trim}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
	}

	[Test]
	public async Task Handler_Trim_ExplicitBoth_TrimsBothSides()
	{
		MString value = MarkupText.Plain("  hello  ");
		MString result = Format($"{value:trim:both}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
	}

	[Test]
	public async Task Handler_Trim_Left_TrimsStart()
	{
		MString value = MarkupText.Plain("  hello  ");
		MString result = Format($"{value:trim:left}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello  ");
	}

	[Test]
	public async Task Handler_Trim_Start_TrimsStart()
	{
		MString value = MarkupText.Plain("  hello  ");
		MString result = Format($"{value:trim:start}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello  ");
	}

	[Test]
	public async Task Handler_Trim_Right_TrimsEnd()
	{
		MString value = MarkupText.Plain("  hello  ");
		MString result = Format($"{value:trim:right}");
		await Assert.That(result.ToPlainText()).IsEqualTo("  hello");
	}

	[Test]
	public async Task Handler_Trim_End_TrimsEnd()
	{
		MString value = MarkupText.Plain("  hello  ");
		MString result = Format($"{value:trim:end}");
		await Assert.That(result.ToPlainText()).IsEqualTo("  hello");
	}

	[Test]
	public async Task Handler_Trim_WithCustomChars()
	{
		MString value = MarkupText.Plain("---hello---");
		MString result = Format($"{value:trim:both:-}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
	}

	[Test]
	public async Task Handler_Trim_PreservesMarkupOnRemainingText()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		MString value = MarkupText.Wrap(red, "  hello  ");
		MString result = Format($"{value:trim}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Runs.Any(r => r.Markups.Count > 0)).IsTrue();
	}

	[Test]
	public async Task Handler_Align_Left_PadsRight()
	{
		MString value = MarkupText.Plain("hi");
		MString result = Format($"{value:align:left:10}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hi        ");
		await Assert.That(result.Length).IsEqualTo(10);
	}

	[Test]
	public async Task Handler_Align_Right_PadsLeft()
	{
		MString value = MarkupText.Plain("hi");
		MString result = Format($"{value:align:right:10}");
		await Assert.That(result.ToPlainText()).IsEqualTo("        hi");
		await Assert.That(result.Length).IsEqualTo(10);
	}

	[Test]
	public async Task Handler_Align_Center_PadsBothSides()
	{
		MString value = MarkupText.Plain("hi");
		MString result = Format($"{value:align:center:10}");
		await Assert.That(result.Length).IsEqualTo(10);
		var plain = result.ToPlainText();
		await Assert.That(plain.Contains("hi")).IsTrue();
		await Assert.That(plain.StartsWith(" ")).IsTrue();
		await Assert.That(plain.EndsWith(" ")).IsTrue();
	}

	[Test]
	public async Task Handler_Align_WithCustomFill()
	{
		MString value = MarkupText.Plain("hi");
		MString result = Format($"{value:align:left:6:-}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hi----");
	}

	[Test]
	public async Task Handler_Align_TruncatesWhenTextTooLong()
	{
		MString value = MarkupText.Plain("hello world");
		MString result = Format($"{value:align:left:5}");
		await Assert.That(result.Length).IsEqualTo(5);
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
	}

	[Test]
	public async Task Handler_Align_PreservesMarkup()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		MString value = MarkupText.Wrap(red, "hi");
		MString result = Format($"{value:align:left:6}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hi    ");
		await Assert.That(result.Runs.Any(r => r.Markups.Count > 0)).IsTrue();
	}

	[Test]
	public async Task Handler_CSharpAlignment_Positive_RightJustifies()
	{
		MString value = MarkupText.Plain("hi");
		MString result = Format($"{value,10}");
		await Assert.That(result.ToPlainText()).IsEqualTo("        hi");
		await Assert.That(result.Length).IsEqualTo(10);
	}

	[Test]
	public async Task Handler_CSharpAlignment_Negative_LeftJustifies()
	{
		MString value = MarkupText.Plain("hi");
		MString result = Format($"{value,-10}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hi        ");
		await Assert.That(result.Length).IsEqualTo(10);
	}

	[Test]
	public async Task Handler_CSharpAlignment_CombinedWithTrimFormat()
	{
		MString value = MarkupText.Plain("  hi  ");
		MString result = Format($"{value,-10:trim}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hi        ");
		await Assert.That(result.Length).IsEqualTo(10);
	}

	[Test]
	public async Task Handler_Color_AppliesAnsiMarkup()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:r}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	[Test]
	public async Task Handler_Color_AppliesHexColor()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:#ff0000}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	[Test]
	public async Task Handler_Color_AppliesHighlightCode()
	{
		MString value = MarkupText.Plain("hello");
		// 'h' sets highlight mode, 'r' applies red — same as ansi("hr", ...) in MUSHCode
		MString result = Format($"{value:color:hr}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		var ansiRender = result.Render(MarkupFormat.Ansi);
		await Assert.That(ansiRender).Contains("\u001b[");
	}

	[Test]
	public async Task Handler_Color_AppliesXtermColor()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:200}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	[Test]
	public async Task Handler_Color_PreservesInnerMarkup()
	{
		var blue = M.Create(foreground: Color.Blue.ToAnsiColor());
		MString inner = MarkupText.Wrap(blue, "hello");
		MString result = Format($"{inner:color:r}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Runs.All(r => r.Markups.Count >= 2)).IsTrue();
	}

	[Test]
	public async Task Handler_Color_UnknownFormatSpecifier_RetainsValue()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:unknown_format}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Runs.All(r => r.Markups.Count == 0)).IsTrue();
	}

	[Test]
	public async Task AnsiCodeParser_RedCode_SetsForegroundRed()
	{
		var markup = AnsiCodeParser.Parse("r");
		await Assert.That(markup.Style.Foreground).IsNotNull();
		await Assert.That(markup.Style.Background).IsNull();
	}

	[Test]
	public async Task AnsiCodeParser_BackgroundCode_SetsBackground()
	{
		var markup = AnsiCodeParser.Parse("R");
		await Assert.That(markup.Style.Background).IsNotNull();
		await Assert.That(markup.Style.Foreground).IsNull();
	}

	[Test]
	public async Task AnsiCodeParser_HexColor_SetsForeground()
	{
		var markup = AnsiCodeParser.Parse("#ff0000");
		await Assert.That(markup.Style.Foreground).IsTypeOf<AnsiColor.Rgb>();
	}

	[Test]
	public async Task AnsiCodeParser_BackgroundHexColor_SetsBackground()
	{
		var markup = AnsiCodeParser.Parse("/#00ff00");
		await Assert.That(markup.Style.Background).IsTypeOf<AnsiColor.Rgb>();
		await Assert.That(markup.Style.Foreground).IsNull();
	}

	[Test]
	public async Task AnsiCodeParser_UnderlineCode_SetsUnderlined()
	{
		var markup = AnsiCodeParser.Parse("u");
		await Assert.That(markup.Style.Underlined).IsTrue();
	}

	[Test]
	public async Task AnsiCodeParser_InvertCode_SetsInverted()
	{
		var markup = AnsiCodeParser.Parse("i");
		await Assert.That(markup.Style.Inverted).IsTrue();
	}

	[Test]
	public async Task AnsiCodeParser_NormalCode_ClearsAllFormatting()
	{
		var markup = AnsiCodeParser.Parse("run");
		await Assert.That(markup.Style.Clear).IsTrue();
		await Assert.That(markup.Style.Foreground).IsNull();
	}

	[Test]
	public async Task AnsiCodeParser_XtermNumber_SetsForeground()
	{
		var markup = AnsiCodeParser.Parse("200");
		await Assert.That(markup.Style.Foreground).IsNotNull();
	}

	[Test]
	public async Task AnsiCodeParser_RgbTriplet_SetsForeground()
	{
		var markup = AnsiCodeParser.Parse("<255 0 0>");
		await Assert.That(markup.Style.Foreground).IsTypeOf<AnsiColor.Rgb>();
	}

	[Test]
	public async Task AnsiCodeParser_BlinkCode_SetsBlink()
	{
		var markup = AnsiCodeParser.Parse("f");
		await Assert.That(markup.Style.Blink).IsTrue();
		await Assert.That(markup.Style.Foreground).IsNull();
	}

	[Test]
	public async Task AnsiCodeParser_XtermPlusPrefix_SetsForeground()
	{
		var markup = AnsiCodeParser.Parse("+xterm200");
		await Assert.That(markup.Style.Foreground).IsNotNull();
	}

	[Test]
	public async Task AnsiCodeParser_BackgroundUpperLetter_SetsBackground()
	{
		// Uppercase 'R' sets red background
		var markup = AnsiCodeParser.Parse("R");
		await Assert.That(markup.Style.Background).IsNotNull();
		await Assert.That(markup.Style.Foreground).IsNull();
	}

	[Test]
	public async Task AnsiCodeParser_BackgroundSlashHex_SetsBackground()
	{
		var markup = AnsiCodeParser.Parse("/#ff0000");
		await Assert.That(markup.Style.Background).IsTypeOf<AnsiColor.Rgb>();
		await Assert.That(markup.Style.Foreground).IsNull();
	}

	[Test]
	public async Task AnsiCodeParser_ResetCode_ClearsAndSetsClearTrue()
	{
		// "n" resets all formatting and sets Clear=true
		var markup = AnsiCodeParser.Parse("rn");
		await Assert.That(markup.Style.Clear).IsTrue();
		await Assert.That(markup.Style.Foreground).IsNull();
	}

	[Test]
	public async Task Handler_Color_BackgroundLetter_SetsBackground()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:R}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	[Test]
	public async Task Handler_Color_BackgroundHex_SetsBackground()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:/#ff0000}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	[Test]
	public async Task Handler_Color_RgbTripletInFormat_SetsForeground()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:<255 0 0>}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
		var run = result.Runs.FirstOrDefault(r => r.Markups.Count > 0);
		await Assert.That(run.Markups.Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task Handler_Color_EmptyCodes_RetainsValueUnchanged()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Runs.All(r => r.Markups.Count == 0)).IsTrue();
	}

	[Test]
	public async Task Handler_Color_XtermPlusPrefix_ProducesAnsi()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:color:+xterm200}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	[Test]
	public async Task Handler_Align_InvalidWidth_RetainsValue()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:align:left:notanumber}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
	}

	[Test]
	public async Task Handler_Trim_EmptyString_ReturnsEmpty()
	{
		MString value = MarkupText.Empty;
		MString result = Format($"{value:trim}");
		await Assert.That(result.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Handler_Align_ZeroWidth_RetainsValue()
	{
		MString value = MarkupText.Plain("hello");
		MString result = Format($"{value:align:left:0}");
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
	}

	[Test]
	public async Task Hilight_String_ProducesBoldBrightWhite()
	{
		// Hilight() uses AnsiCodeParser.Parse("hw") → AnsiColor.ANSI([1, 37])
		// which renders as ESC[1;37m (bold + SGR bright white).
		MString result = "hello".Hilight();
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		var ansiOut = result.Render(MarkupFormat.Ansi);
		await Assert.That(ansiOut).Contains("\u001b[");
		// Bold (1) and white (37) SGR codes must be present
		await Assert.That(ansiOut).Contains("1;37");
	}

	[Test]
	public async Task Hilight_MString_ProducesBoldBrightWhite()
	{
		MString inner = MarkupText.Plain("hello");
		MString result = inner.Hilight();
		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
		var ansiOut = result.Render(MarkupFormat.Ansi);
		await Assert.That(ansiOut).Contains("1;37");
	}

	[Test]
	public async Task Hilight_RendersIdenticallyToColorHw()
	{
		MString value = MarkupText.Plain("hello");
		MString fromHilight = value.Hilight();
		MString fromColorHw = Format($"{value:color:hw}");
		await Assert.That(fromHilight.Render(MarkupFormat.Ansi)).IsEqualTo(fromColorHw.Render(MarkupFormat.Ansi));
	}
}
