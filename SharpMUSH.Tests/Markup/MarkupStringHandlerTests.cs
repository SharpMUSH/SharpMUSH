using System.Drawing;
using ANSILibrary;
using MarkupString;
using MarkupString.MarkupImplementation;
using AMS = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using static MarkupString.MStringInterpolation;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Tests for the <see cref="MarkupStringHandler"/> interpolated string handler.
///
/// Because <see cref="MString"/> is an F# class and cannot expose a C# constructor
/// accepting a <see langword="ref struct"/> handler type, the interpolated string is
/// routed through <see cref="MStringInterpolation.Format"/>:
/// <code>
/// MString result = Format($"Hello, {bold}! Count: {42}.");
/// </code>
/// </summary>
public class MarkupStringHandlerTests
{
    // ── Plain-text holes ───────────────────────────────────────────

    [Test]
    public async Task Handler_StringHole_ProducesPlainText()
    {
        string name = "World";
        MString result = Format($"Hello, {name}!");

        await Assert.That(result.ToPlainText()).IsEqualTo("Hello, World!");
        await Assert.That(result.Length).IsEqualTo(13);
        await Assert.That(result.Runs.All(r => r.Markups.Length == 0)).IsTrue();
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

    // ── Markup preservation ────────────────────────────────────────

    [Test]
    public async Task Handler_MStringHole_PreservesMarkupRuns()
    {
	var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
        MString bold = AMS.MarkupSingle(redMarkup, "world");

        MString result = Format($"Hello, {bold}!");

        await Assert.That(result.ToPlainText()).IsEqualTo("Hello, world!");
        // Should have at least two runs: plain "Hello, " and marked "world"
        await Assert.That(result.Runs.Length).IsGreaterThanOrEqualTo(2);

        // The run covering "world" (offset 7, length 5) must carry the red markup
        var markedRun = result.Runs.FirstOrDefault(r => r.Start == 7 && r.Length == 5);
        await Assert.That(markedRun.Markups.Length).IsEqualTo(1);
        await Assert.That(markedRun.Markups[0]).IsEqualTo(redMarkup);
    }

    [Test]
    public async Task Handler_MultipleMStringHoles_PreservesAllMarkups()
    {
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blue = M.Create(foreground: new AnsiColor.RGB(Color.Blue));

		MString redWord = AMS.MarkupSingle(red, "red");
		MString blueWord = AMS.MarkupSingle(blue, "blue");

        MString result = Format($"Color: {redWord} and {blueWord}.");

        await Assert.That(result.ToPlainText()).IsEqualTo("Color: red and blue.");

        // Both markup runs must survive into the ANSI render output
        var ansiOutput = result.Render("ansi");
        await Assert.That(ansiOutput).Contains("red");
        await Assert.That(ansiOutput).Contains("blue");
        await Assert.That(ansiOutput).Contains("\u001b["); // ANSI escape present
    }

    [Test]
    public async Task Handler_MStringHole_AnsiRenderMatchesConcatMany()
    {
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		MString marked = AMS.MarkupSingle(red, "world");

		// Handler path
		MString fromHandler = Format($"Hello, {marked}!");

        // Equivalent manual construction
        MString manual = AMS.concatMany([AMS.single("Hello, "), marked, AMS.single("!")]);

        await Assert.That(fromHandler.ToPlainText()).IsEqualTo(manual.ToPlainText());
        await Assert.That(fromHandler.Render("ansi")).IsEqualTo(manual.Render("ansi"));
        await Assert.That(fromHandler.Render("html")).IsEqualTo(manual.Render("html"));
    }

    // ── Mixed holes ────────────────────────────────────────────────

    [Test]
    public async Task Handler_MixedHoles_CombinesCorrectly()
    {
        var boldMarkup = M.Create(bold: true);
        MString name = AMS.MarkupSingle(boldMarkup, "Alice");
        int score = 99;

        MString result = Format($"Player {name} scored {score} points.");

        await Assert.That(result.ToPlainText()).IsEqualTo("Player Alice scored 99 points.");

        // The bold run for "Alice" must be preserved at offset 7, length 5
        var boldRun = result.Runs.FirstOrDefault(r => r.Start == 7 && r.Length == 5);
        await Assert.That(boldRun.Markups.Length).IsEqualTo(1);
    }

    // ── Edge cases ─────────────────────────────────────────────────

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
        MString empty = AMS.empty();
        MString result = Format($"before{empty}after");

        await Assert.That(result.ToPlainText()).IsEqualTo("beforeafter");
    }

    [Test]
    public async Task Handler_ConsecutiveMStringHoles_MaintainOrder()
    {
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blue = M.Create(foreground: new AnsiColor.RGB(Color.Blue));
		var green = M.Create(foreground: new AnsiColor.RGB(Color.Green));

        MString a = AMS.MarkupSingle(red, "R");
        MString b = AMS.MarkupSingle(blue, "G");
        MString c = AMS.MarkupSingle(green, "B");

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
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		MString marked = AMS.MarkupSingle(red, "mid");

		MString result = Format($"before {marked} after");

        for (int i = 1; i < result.Runs.Length; i++)
            await Assert.That(result.Runs[i].Start).IsGreaterThan(result.Runs[i - 1].Start);
    }

    [Test]
    public async Task Handler_SingleHoleNoLiterals_RetainsMarkup()
    {
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		MString marked = AMS.MarkupSingle(red, "only");

        MString result = Format($"{marked}");

        await Assert.That(result.ToPlainText()).IsEqualTo("only");
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Markups.Length).IsEqualTo(1);
    }

    // ── Trim format specifier ─────────────────────────────────────────

    [Test]
    public async Task Handler_Trim_DefaultTrimsBothSides()
    {
        MString value = AMS.single("  hello  ");
        MString result = Format($"{value:trim}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
    }

    [Test]
    public async Task Handler_Trim_ExplicitBoth_TrimsBothSides()
    {
        MString value = AMS.single("  hello  ");
        MString result = Format($"{value:trim:both}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
    }

    [Test]
    public async Task Handler_Trim_Left_TrimsStart()
    {
        MString value = AMS.single("  hello  ");
        MString result = Format($"{value:trim:left}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello  ");
    }

    [Test]
    public async Task Handler_Trim_Start_TrimsStart()
    {
        MString value = AMS.single("  hello  ");
        MString result = Format($"{value:trim:start}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello  ");
    }

    [Test]
    public async Task Handler_Trim_Right_TrimsEnd()
    {
        MString value = AMS.single("  hello  ");
        MString result = Format($"{value:trim:right}");
        await Assert.That(result.ToPlainText()).IsEqualTo("  hello");
    }

    [Test]
    public async Task Handler_Trim_End_TrimsEnd()
    {
        MString value = AMS.single("  hello  ");
        MString result = Format($"{value:trim:end}");
        await Assert.That(result.ToPlainText()).IsEqualTo("  hello");
    }

    [Test]
    public async Task Handler_Trim_WithCustomChars()
    {
        MString value = AMS.single("---hello---");
        MString result = Format($"{value:trim:both:-}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
    }

    [Test]
    public async Task Handler_Trim_PreservesMarkupOnRemainingText()
    {
        var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
        MString value = AMS.MarkupSingle(red, "  hello  ");
        MString result = Format($"{value:trim}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
        await Assert.That(result.Runs.Any(r => r.Markups.Length > 0)).IsTrue();
    }

    // ── Align format specifier ────────────────────────────────────────

    [Test]
    public async Task Handler_Align_Left_PadsRight()
    {
        MString value = AMS.single("hi");
        MString result = Format($"{value:align:left:10}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hi        ");
        await Assert.That(result.Length).IsEqualTo(10);
    }

    [Test]
    public async Task Handler_Align_Right_PadsLeft()
    {
        MString value = AMS.single("hi");
        MString result = Format($"{value:align:right:10}");
        await Assert.That(result.ToPlainText()).IsEqualTo("        hi");
        await Assert.That(result.Length).IsEqualTo(10);
    }

    [Test]
    public async Task Handler_Align_Center_PadsBothSides()
    {
        MString value = AMS.single("hi");
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
        MString value = AMS.single("hi");
        MString result = Format($"{value:align:left:6:-}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hi----");
    }

    [Test]
    public async Task Handler_Align_TruncatesWhenTextTooLong()
    {
        MString value = AMS.single("hello world");
        MString result = Format($"{value:align:left:5}");
        await Assert.That(result.Length).IsEqualTo(5);
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
    }

    [Test]
    public async Task Handler_Align_PreservesMarkup()
    {
        var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
        MString value = AMS.MarkupSingle(red, "hi");
        MString result = Format($"{value:align:left:6}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hi    ");
        await Assert.That(result.Runs.Any(r => r.Markups.Length > 0)).IsTrue();
    }

    // ── C# alignment specifier ────────────────────────────────────────

    [Test]
    public async Task Handler_CSharpAlignment_Positive_RightJustifies()
    {
        MString value = AMS.single("hi");
        MString result = Format($"{value,10}");
        await Assert.That(result.ToPlainText()).IsEqualTo("        hi");
        await Assert.That(result.Length).IsEqualTo(10);
    }

    [Test]
    public async Task Handler_CSharpAlignment_Negative_LeftJustifies()
    {
        MString value = AMS.single("hi");
        MString result = Format($"{value,-10}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hi        ");
        await Assert.That(result.Length).IsEqualTo(10);
    }

    [Test]
    public async Task Handler_CSharpAlignment_CombinedWithTrimFormat()
    {
        MString value = AMS.single("  hi  ");
        MString result = Format($"{value,-10:trim}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hi        ");
        await Assert.That(result.Length).IsEqualTo(10);
    }

    // ── Color format specifier ────────────────────────────────────────

    [Test]
    public async Task Handler_Color_AppliesAnsiMarkup()
    {
        MString value = AMS.single("hello");
        MString result = Format($"{value:color:r}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
        await Assert.That(result.Render("ansi")).Contains("\u001b[");
    }

    [Test]
    public async Task Handler_Color_AppliesHexColor()
    {
        MString value = AMS.single("hello");
        MString result = Format($"{value:color:#ff0000}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
        await Assert.That(result.Render("ansi")).Contains("\u001b[");
    }

    [Test]
    public async Task Handler_Color_AppliesHighlightCode()
    {
        MString value = AMS.single("hello");
        // 'h' sets highlight mode, 'r' applies red — same as ansi("hr", ...) in MUSHCode
        MString result = Format($"{value:color:hr}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
        var ansiRender = result.Render("ansi");
        await Assert.That(ansiRender).Contains("\u001b[");
    }

    [Test]
    public async Task Handler_Color_AppliesXtermColor()
    {
        MString value = AMS.single("hello");
        MString result = Format($"{value:color:200}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
        await Assert.That(result.Render("ansi")).Contains("\u001b[");
    }

    [Test]
    public async Task Handler_Color_PreservesInnerMarkup()
    {
        var blue = M.Create(foreground: new AnsiColor.RGB(Color.Blue));
        MString inner = AMS.MarkupSingle(blue, "hello");
        MString result = Format($"{inner:color:r}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
        // Should have two markup layers
        await Assert.That(result.Runs.All(r => r.Markups.Length >= 2)).IsTrue();
    }

    [Test]
    public async Task Handler_Color_UnknownFormatSpecifier_RetainsValue()
    {
        MString value = AMS.single("hello");
        MString result = Format($"{value:unknown_format}");
        await Assert.That(result.ToPlainText()).IsEqualTo("hello");
        await Assert.That(result.Runs.All(r => r.Markups.Length == 0)).IsTrue();
    }

    // ── AnsiCodeParser standalone tests ─────────────────────────────

    [Test]
    public async Task AnsiCodeParser_RedCode_SetsForegroundRed()
    {
        var markup = AnsiCodeParser.ParseCodes("r");
        await Assert.That(markup.Details.Foreground).IsNotEqualTo(AnsiColor.NoAnsi.Instance);
        await Assert.That(markup.Details.Background).IsEqualTo(AnsiColor.NoAnsi.Instance);
    }

    [Test]
    public async Task AnsiCodeParser_BackgroundCode_SetsBackground()
    {
        var markup = AnsiCodeParser.ParseCodes("R");
        await Assert.That(markup.Details.Background).IsNotEqualTo(AnsiColor.NoAnsi.Instance);
        await Assert.That(markup.Details.Foreground).IsEqualTo(AnsiColor.NoAnsi.Instance);
    }

    [Test]
    public async Task AnsiCodeParser_HexColor_SetsForeground()
    {
        var markup = AnsiCodeParser.ParseCodes("#ff0000");
        await Assert.That(markup.Details.Foreground).IsTypeOf<AnsiColor.RGB>();
    }

    [Test]
    public async Task AnsiCodeParser_BackgroundHexColor_SetsBackground()
    {
        var markup = AnsiCodeParser.ParseCodes("/#00ff00");
        await Assert.That(markup.Details.Background).IsTypeOf<AnsiColor.RGB>();
        await Assert.That(markup.Details.Foreground).IsEqualTo(AnsiColor.NoAnsi.Instance);
    }

    [Test]
    public async Task AnsiCodeParser_UnderlineCode_SetsUnderlined()
    {
        var markup = AnsiCodeParser.ParseCodes("u");
        await Assert.That(markup.Details.Underlined).IsTrue();
    }

    [Test]
    public async Task AnsiCodeParser_InvertCode_SetsInverted()
    {
        var markup = AnsiCodeParser.ParseCodes("i");
        await Assert.That(markup.Details.Inverted).IsTrue();
    }

    [Test]
    public async Task AnsiCodeParser_NormalCode_ClearsAllFormatting()
    {
        var markup = AnsiCodeParser.ParseCodes("run");
        await Assert.That(markup.Details.Clear).IsTrue();
        await Assert.That(markup.Details.Foreground).IsEqualTo(AnsiColor.NoAnsi.Instance);
    }

    [Test]
    public async Task AnsiCodeParser_XtermNumber_SetsForeground()
    {
        var markup = AnsiCodeParser.ParseCodes("200");
        await Assert.That(markup.Details.Foreground).IsNotEqualTo(AnsiColor.NoAnsi.Instance);
    }

    [Test]
    public async Task AnsiCodeParser_RgbTriplet_SetsForeground()
    {
        var markup = AnsiCodeParser.ParseCodes("<255 0 0>");
        await Assert.That(markup.Details.Foreground).IsTypeOf<AnsiColor.RGB>();
    }
}
