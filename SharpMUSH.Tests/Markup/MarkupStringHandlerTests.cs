using System.Drawing;
using ANSILibrary;
using MarkupString;
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
}
