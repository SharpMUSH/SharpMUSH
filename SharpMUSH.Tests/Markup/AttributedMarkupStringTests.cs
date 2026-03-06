using System.Collections.Immutable;
using System.Drawing;
using Microsoft.FSharp.Core;
using MarkupString;
using AMS = MarkupString.MarkupStringModule;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using H = MarkupString.MarkupImplementation.HtmlMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Unit tests for the NSAttributedString-inspired MarkupString.
/// </summary>
public class MarkupStringTests
{
	// ── Construction ───────────────────────────────────────────────

	[Test]
	public async Task Single_PlainText_CreatesCorrectString()
	{
		var ams = AMS.single("Hello, World!");

		await Assert.That(ams.ToPlainText()).IsEqualTo("Hello, World!");
		await Assert.That(ams.Length).IsEqualTo(13);
		await Assert.That(ams.ToString()).IsEqualTo("Hello, World!");
	}

	[Test]
	public async Task Empty_CreatesEmptyString()
	{
		var ams = AMS.empty();

		await Assert.That(ams.ToPlainText()).IsEqualTo("");
		await Assert.That(ams.Length).IsEqualTo(0);
		await Assert.That(ams.Runs).IsEmpty();
	}

	[Test]
	public async Task MarkupSingle_WithAnsiMarkup_PreservesPlainText()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		await Assert.That(ams.ToPlainText()).IsEqualTo("Red Text");
		await Assert.That(ams.Length).IsEqualTo(8);
		await Assert.That(ams.Runs.Length).IsEqualTo(1);
		await Assert.That(ams.Runs[0].Markups.Length).IsEqualTo(1);
	}

	[Test]
	public async Task MarkupSingle_WithHtmlMarkup_PreservesPlainText()
	{
		var htmlMarkup = H.Create("b");
		var ams = AMS.markupSingle(htmlMarkup, "Bold Text");

		await Assert.That(ams.ToPlainText()).IsEqualTo("Bold Text");
		await Assert.That(ams.Length).IsEqualTo(9);
	}

	[Test]
	public async Task MarkupSingleMulti_WithMixedMarkups_StoresAll()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var htmlMarkup = H.Create("b");
		var ams = AMS.markupSingleMulti(
			ImmutableArray.Create<MarkupImplementation.Markup>(redMarkup, htmlMarkup),
			"Mixed Markup");

		await Assert.That(ams.ToPlainText()).IsEqualTo("Mixed Markup");
		await Assert.That(ams.Runs[0].Markups.Length).IsEqualTo(2);
	}

	// ── Concat ─────────────────────────────────────────────────────

	[Test]
	public async Task Concat_TwoPlainStrings_CombinesText()
	{
		var a = AMS.single("Hello");
		var b = AMS.single(" World");
		var result = AMS.concat(a, b);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(result.Length).IsEqualTo(11);
	}

	[Test]
	public async Task Concat_PlainAndMarkup_PreservesBothRuns()
	{
		var plain = AMS.single("Hello ");
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var marked = AMS.markupSingle(redMarkup, "World");
		var result = AMS.concat(plain, marked);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(result.Runs.Length).IsEqualTo(2);
		await Assert.That(result.Runs[0].Markups.Length).IsEqualTo(0);
		await Assert.That(result.Runs[1].Markups.Length).IsEqualTo(1);
	}

	[Test]
	public async Task Concat_WithEmpty_ReturnsOther()
	{
		var ams = AMS.single("Hello");
		var e = AMS.empty();

		var result1 = AMS.concat(ams, e);
		var result2 = AMS.concat(e, ams);

		await Assert.That(result1.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(result2.ToPlainText()).IsEqualTo("Hello");
	}

	// ── ConcatMany ────────────────────────────────────────────────

	[Test]
	public async Task ConcatMany_MultiplePlainStrings_CombinesAll()
	{
		var items = new[]
		{
			AMS.single("Hello"),
			AMS.single(" "),
			AMS.single("World"),
			AMS.single("!")
		};
		var result = AMS.concatMany(items);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello World!");
		await Assert.That(result.Length).IsEqualTo(12);
	}

	[Test]
	public async Task ConcatMany_WithMarkups_PreservesAllRuns()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var green = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Green));

		var items = new[]
		{
			AMS.markupSingle(red, "Red"),
			AMS.markupSingle(blue, "Blue"),
			AMS.markupSingle(green, "Green")
		};
		var result = AMS.concatMany(items);

		await Assert.That(result.ToPlainText()).IsEqualTo("RedBlueGreen");
		await Assert.That(result.Runs.Length).IsEqualTo(3);
		await Assert.That(result.Runs[0].Start).IsEqualTo(0);
		await Assert.That(result.Runs[0].Length).IsEqualTo(3);
		await Assert.That(result.Runs[1].Start).IsEqualTo(3);
		await Assert.That(result.Runs[1].Length).IsEqualTo(4);
		await Assert.That(result.Runs[2].Start).IsEqualTo(7);
		await Assert.That(result.Runs[2].Length).IsEqualTo(5);
	}

	[Test]
	public async Task ConcatMany_EmptySequence_ReturnsEmpty()
	{
		var result = AMS.concatMany(Array.Empty<AMS.MarkupString>());

		await Assert.That(result.ToPlainText()).IsEqualTo("");
		await Assert.That(result.Length).IsEqualTo(0);
	}

	[Test]
	public async Task ConcatMany_SingleItem_ReturnsSame()
	{
		var item = AMS.single("Hello");
		var result = AMS.concatMany(new[] { item });

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello");
	}

	[Test]
	public async Task ConcatMany_SkipsEmptyItems()
	{
		var items = new[]
		{
			AMS.single("Hello"),
			AMS.empty(),
			AMS.single(" World"),
			AMS.empty()
		};
		var result = AMS.concatMany(items);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello World");
	}

	[Test]
	public async Task ConcatMany_MatchesBinaryConcat()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var green = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Green));

		var a = AMS.markupSingle(red, "AA");
		var b = AMS.markupSingle(blue, "BB");
		var c = AMS.markupSingle(green, "CC");

		var binaryResult = AMS.concat(AMS.concat(a, b), c);
		var manyResult = AMS.concatMany(new[] { a, b, c });

		await Assert.That(manyResult.ToPlainText()).IsEqualTo(binaryResult.ToPlainText());
		await Assert.That(manyResult.Runs.Length).IsEqualTo(binaryResult.Runs.Length);
		await Assert.That(manyResult.Render("ansi")).IsEqualTo(binaryResult.Render("ansi"));
	}

	// ── Substring ──────────────────────────────────────────────────

	[Test]
	public async Task Substring_PlainText_ExtractsCorrectRange()
	{
		var ams = AMS.single("Hello, World!");
		var sub = AMS.substring(7, 5, ams);

		await Assert.That(sub.ToPlainText()).IsEqualTo("World");
		await Assert.That(sub.Length).IsEqualTo(5);
	}

	[Test]
	public async Task Substring_AcrossRuns_ClipsRuns()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var part1 = AMS.markupSingle(red, "Hello");
		var part2 = AMS.markupSingle(blue, " World");
		var combined = AMS.concat(part1, part2);

		// Take "llo W" which spans both runs
		var sub = AMS.substring(2, 5, combined);

		await Assert.That(sub.ToPlainText()).IsEqualTo("llo W");
		await Assert.That(sub.Length).IsEqualTo(5);
		// Should have 2 runs, each clipped
		await Assert.That(sub.Runs.Length).IsEqualTo(2);
	}

	[Test]
	public async Task Substring_BeyondLength_ReturnsEmpty()
	{
		var ams = AMS.single("Hi");
		var sub = AMS.substring(10, 5, ams);

		await Assert.That(sub.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Substring_ZeroLength_ReturnsEmpty()
	{
		var ams = AMS.single("Hello");
		var sub = AMS.substring(0, 0, ams);

		await Assert.That(sub.Length).IsEqualTo(0);
	}

	// ── Split ──────────────────────────────────────────────────────

	[Test]
	public async Task Split_PlainText_SplitsCorrectly()
	{
		var ams = AMS.single("a,b,c");
		var parts = AMS.split(",", ams);

		await Assert.That(parts.Length).IsEqualTo(3);
		await Assert.That(parts[0].ToPlainText()).IsEqualTo("a");
		await Assert.That(parts[1].ToPlainText()).IsEqualTo("b");
		await Assert.That(parts[2].ToPlainText()).IsEqualTo("c");
	}

	[Test]
	public async Task Split_NoDelimiter_ReturnsSingle()
	{
		var ams = AMS.single("hello");
		var parts = AMS.split(",", ams);

		await Assert.That(parts.Length).IsEqualTo(1);
		await Assert.That(parts[0].ToPlainText()).IsEqualTo("hello");
	}

	// ── Trim ───────────────────────────────────────────────────────

	[Test]
	public async Task Trim_BothSides_TrimsCorrectly()
	{
		var ams = AMS.single("  hello  ");
		var result = AMS.trim(ams, " ", MarkupStringModule.TrimType.TrimBoth);

		await Assert.That(result.ToPlainText()).IsEqualTo("hello");
	}

	[Test]
	public async Task Trim_StartOnly_TrimsCorrectly()
	{
		var ams = AMS.single("  hello  ");
		var result = AMS.trim(ams, " ", MarkupStringModule.TrimType.TrimStart);

		await Assert.That(result.ToPlainText()).IsEqualTo("hello  ");
	}

	[Test]
	public async Task Trim_EndOnly_TrimsCorrectly()
	{
		var ams = AMS.single("  hello  ");
		var result = AMS.trim(ams, " ", MarkupStringModule.TrimType.TrimEnd);

		await Assert.That(result.ToPlainText()).IsEqualTo("  hello");
	}

	// ── Optimize ───────────────────────────────────────────────────

	[Test]
	public async Task Optimize_AdjacentSameMarkup_MergesRuns()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var part1 = AMS.markupSingle(red, "Hello");
		var part2 = AMS.markupSingle(red, " World");
		var combined = AMS.concat(part1, part2);

		// Before optimize: 2 runs
		await Assert.That(combined.Runs.Length).IsEqualTo(2);

		var optimized = AMS.optimize(combined);

		// After optimize: 1 merged run
		await Assert.That(optimized.Runs.Length).IsEqualTo(1);
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
	}

	[Test]
	public async Task Optimize_DifferentMarkup_DoesNotMerge()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var part1 = AMS.markupSingle(red, "Hello");
		var part2 = AMS.markupSingle(blue, " World");
		var combined = AMS.concat(part1, part2);

		var optimized = AMS.optimize(combined);

		await Assert.That(optimized.Runs.Length).IsEqualTo(2);
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
	}

	// ── Render ─────────────────────────────────────────────────────

	[Test]
	public async Task Render_AnsiFormat_ContainsEscapeCodes()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var ansiOutput = ams.Render("ansi");

		await Assert.That(ansiOutput).Contains("\u001b[");
		await Assert.That(ansiOutput).Contains("Red Text");
	}

	[Test]
	public async Task Render_HtmlFormat_ContainsSpanTags()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var ams = AMS.markupSingle(redMarkup, "Red Text");

		var htmlOutput = ams.Render("html");

		await Assert.That(htmlOutput).Contains("<span");
		await Assert.That(htmlOutput).Contains("color: #ff0000");
		await Assert.That(htmlOutput).Contains("Red Text");
		await Assert.That(htmlOutput).Contains("</span>");
	}

	[Test]
	public async Task Render_HtmlFormat_HtmlEncodesText()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "<script>alert('xss')</script>");

		var htmlOutput = ams.Render("html");

		await Assert.That(htmlOutput).DoesNotContain("<script>");
		await Assert.That(htmlOutput).Contains("&lt;script&gt;");
	}

	[Test]
	public async Task Render_HtmlFormat_BoldMarkup_ReturnsCssClass()
	{
		var boldMarkup = M.Create(bold: true);
		var ams = AMS.markupSingle(boldMarkup, "Bold Text");

		var htmlOutput = ams.Render("html");

		await Assert.That(htmlOutput).Contains("ms-bold");
	}

	[Test]
	public async Task Render_PlainText_NoFormatting()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Text");

		var plainText = ams.ToPlainText();

		await Assert.That(plainText).IsEqualTo("Text");
		await Assert.That(plainText).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_HtmlFormat_HtmlMarkup_RendersHtmlTags()
	{
		var htmlMarkup = H.Create("b");
		var ams = AMS.markupSingle(htmlMarkup, "Bold");

		var htmlOutput = ams.Render("html");

		await Assert.That(htmlOutput).IsEqualTo("<b>Bold</b>");
	}

	// ── Conversion ─────────────────────────────────────────────────

	[Test]
	public async Task FromMarkupString_PlainText_ConvertsFaithfully()
	{
		var ms = global::MarkupString.TreeMarkupStringModule.single("Hello, World!");
		var ams = AMS.fromMarkupString(ms);

		await Assert.That(ams.ToPlainText()).IsEqualTo("Hello, World!");
		await Assert.That(ams.Length).IsEqualTo(13);
	}

	[Test]
	public async Task FromMarkupString_MarkupText_PreservesMarkup()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ms = global::MarkupString.TreeMarkupStringModule.markupSingle(redMarkup, "Red Text");
		var ams = AMS.fromMarkupString(ms);

		await Assert.That(ams.ToPlainText()).IsEqualTo("Red Text");
		await Assert.That(ams.Runs.Length).IsEqualTo(1);
		await Assert.That(ams.Runs[0].Markups.Length).IsEqualTo(1);
	}

	[Test]
	public async Task FromMarkupString_NestedMarkup_FlattensToCombinedRuns()
	{
		// Create: red(hello blue(world))
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var innerMs = global::MarkupString.TreeMarkupStringModule.markupSingle(blueMarkup, "world");
		var outerMs = new global::MarkupString.TreeMarkupStringModule.TreeMarkupString(
			FSharpOption<MarkupImplementation.Markup>.Some(redMarkup),
			Microsoft.FSharp.Collections.ListModule.OfSeq(new global::MarkupString.TreeMarkupStringModule.Content[]
			{
				global::MarkupString.TreeMarkupStringModule.Content.NewText("hello "),
				global::MarkupString.TreeMarkupStringModule.Content.NewMarkupText(innerMs)
			})
		);

		var ams = AMS.fromMarkupString(outerMs);

		await Assert.That(ams.ToPlainText()).IsEqualTo("hello world");
		await Assert.That(ams.Runs.Length).IsEqualTo(2);
		// "hello " has only the red markup
		await Assert.That(ams.Runs[0].Markups.Length).IsEqualTo(1);
		// "world" has red + blue (parent + child)
		await Assert.That(ams.Runs[1].Markups.Length).IsEqualTo(2);
	}

	[Test]
	public async Task ToMarkupString_PlainText_ConvertsFaithfully()
	{
		var ams = AMS.single("Hello");
		var ms = AMS.toMarkupString(ams);

		await Assert.That(ms.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(ms.Length).IsEqualTo(5);
	}

	[Test]
	public async Task ToMarkupString_MarkupText_PreservesMarkup()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red");
		var ms = AMS.toMarkupString(ams);

		await Assert.That(ms.ToPlainText()).IsEqualTo("Red");
		// Should contain ANSI escape codes when rendered
		await Assert.That(ms.ToString()).Contains("\u001b[");
	}

	[Test]
	public async Task RoundTrip_MarkupString_PreservesText()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var original = global::MarkupString.TreeMarkupStringModule.markupSingle(redMarkup, "Test");
		var ams = AMS.fromMarkupString(original);
		var roundTrip = AMS.toMarkupString(ams);

		await Assert.That(roundTrip.ToPlainText()).IsEqualTo(original.ToPlainText());
		// Verify markup is preserved through conversion (both should contain ANSI codes)
		await Assert.That(roundTrip.ToString()).Contains("\u001b[");
		await Assert.That(roundTrip.ToString()).Contains("Test");
	}

	// ── IndexOf ────────────────────────────────────────────────────

	[Test]
	public async Task IndexOf_Found_ReturnsCorrectIndex()
	{
		var ams = AMS.single("Hello, World!");
		var index = AMS.indexOf(ams, "World");

		await Assert.That(index).IsEqualTo(7);
	}

	[Test]
	public async Task IndexOf_NotFound_ReturnsNegativeOne()
	{
		var ams = AMS.single("Hello");
		var index = AMS.indexOf(ams, "xyz");

		await Assert.That(index).IsEqualTo(-1);
	}

	// ── Remove ─────────────────────────────────────────────────────

	[Test]
	public async Task Remove_MiddleSection_RemovesCorrectly()
	{
		var ams = AMS.single("Hello World");
		var result = AMS.remove(ams, 5, 1);

		await Assert.That(result.ToPlainText()).IsEqualTo("HelloWorld");
	}

	// ── Replace ────────────────────────────────────────────────────

	[Test]
	public async Task Replace_MiddleSection_ReplacesCorrectly()
	{
		var ams = AMS.single("Hello World");
		var replacement = AMS.single("Beautiful ");
		var result = AMS.replace(ams, replacement, 6, 0);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello Beautiful World");
	}

	// ── Repeat ─────────────────────────────────────────────────────

	[Test]
	public async Task Repeat_ThreeTimes_RepeatsCorrectly()
	{
		var ams = AMS.single("ab");
		var result = AMS.repeat(ams, 3);

		await Assert.That(result.ToPlainText()).IsEqualTo("ababab");
		await Assert.That(result.Length).IsEqualTo(6);
	}

	[Test]
	public async Task Repeat_ZeroTimes_ReturnsEmpty()
	{
		var ams = AMS.single("ab");
		var result = AMS.repeat(ams, 0);

		await Assert.That(result.Length).IsEqualTo(0);
	}

	// ── InsertAt ───────────────────────────────────────────────────

	[Test]
	public async Task InsertAt_Middle_InsertsCorrectly()
	{
		var ams = AMS.single("HelloWorld");
		var insert = AMS.single(" ");
		var result = AMS.insertAt(ams, insert, 5);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello World");
	}

	// ── EvaluateWith ───────────────────────────────────────────────

	[Test]
	public async Task EvaluateWith_CustomEvaluator_WorksCorrectly()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Test");

		Func<FSharpOption<MarkupImplementation.Markup>, string, string> evaluator = (markupType, text) =>
		{
			return markupType switch
			{
				not null => $"[{text}]",
				_ => text
			};
		};

		var result = ams.EvaluateWith(evaluator);

		await Assert.That(result).IsEqualTo("[Test]");
	}

	// ── Equality ───────────────────────────────────────────────────

	[Test]
	public async Task Equals_SameText_ReturnsTrue()
	{
		var a = AMS.single("Hello");
		var b = AMS.single("Hello");

		await Assert.That(a.Equals(b)).IsTrue();
	}

	[Test]
	public async Task Equals_DifferentText_ReturnsFalse()
	{
		var a = AMS.single("Hello");
		var b = AMS.single("World");

		await Assert.That(a.Equals(b)).IsFalse();
	}

	[Test]
	public async Task Equals_CompareWithString_ReturnsTrue()
	{
		var ams = AMS.single("Hello");

		await Assert.That(ams.Equals("Hello")).IsTrue();
	}

	// ── Pad ────────────────────────────────────────────────────────

	[Test]
	public async Task Pad_Right_PadsCorrectly()
	{
		var ams = AMS.single("Hi");
		var padStr = AMS.single(" ");
		var result = AMS.pad(ams, padStr, 5, MarkupStringModule.PadType.Right, MarkupStringModule.TruncationType.Overflow);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hi   ");
		await Assert.That(result.Length).IsEqualTo(5);
	}

	[Test]
	public async Task Pad_Left_PadsCorrectly()
	{
		var ams = AMS.single("Hi");
		var padStr = AMS.single(" ");
		var result = AMS.pad(ams, padStr, 5, MarkupStringModule.PadType.Left, MarkupStringModule.TruncationType.Overflow);

		await Assert.That(result.ToPlainText()).IsEqualTo("   Hi");
	}

	[Test]
	public async Task Pad_Center_PadsCorrectly()
	{
		var ams = AMS.single("Hi");
		var padStr = AMS.single("-");
		var result = AMS.pad(ams, padStr, 6, MarkupStringModule.PadType.Center, MarkupStringModule.TruncationType.Overflow);

		await Assert.That(result.ToPlainText()).IsEqualTo("--Hi--");
	}

	// ── MultipleWithDelimiter ──────────────────────────────────────

	[Test]
	public async Task MultipleWithDelimiter_JoinsCorrectly()
	{
		var items = new[]
		{
			AMS.single("a"),
			AMS.single("b"),
			AMS.single("c")
		};
		var delimiter = AMS.single(", ");
		var result = AMS.multipleWithDelimiter(delimiter, items);

		await Assert.That(result.ToPlainText()).IsEqualTo("a, b, c");
	}

	// ── Apply ──────────────────────────────────────────────────────

	[Test]
	public async Task Apply_ToUpper_TransformsText()
	{
		var ams = AMS.single("hello");
		Func<string, string> transform = s => s.ToUpper();
		var result = AMS.apply(ams, Microsoft.FSharp.Core.FuncConvert.FromFunc(transform));

		await Assert.That(result.ToPlainText()).IsEqualTo("HELLO");
	}

	// ── Immutability guarantees ───────────────────────────────────

	[Test]
	public async Task Immutability_RunsAreImmutableArray()
	{
		var ams = AMS.single("Hello");

		// Runs property returns ImmutableArray — a value type that cannot be mutated
		var runs = ams.Runs;
		await Assert.That(runs).IsTypeOf<ImmutableArray<AMS.AttributeRun>>();
	}

	[Test]
	public async Task Immutability_MarkupsAreImmutableArray()
	{
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var ams = AMS.markupSingle(redMarkup, "Red");

		// Markups within each run are ImmutableArray — cannot be mutated
		var markups = ams.Runs[0].Markups;
		await Assert.That(markups).IsTypeOf<ImmutableArray<MarkupImplementation.Markup>>();
	}

	[Test]
	public async Task Immutability_ConcatDoesNotMutateOriginals()
	{
		var a = AMS.single("Hello");
		var b = AMS.single(" World");
		var combined = AMS.concat(a, b);

		// Originals should be unchanged
		await Assert.That(a.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(a.Length).IsEqualTo(5);
		await Assert.That(b.ToPlainText()).IsEqualTo(" World");
		await Assert.That(b.Length).IsEqualTo(6);

		// Combined is a new instance
		await Assert.That(combined.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(combined.Length).IsEqualTo(11);
	}

	[Test]
	public async Task Immutability_SubstringDoesNotMutateOriginal()
	{
		var ams = AMS.single("Hello, World!");
		var sub = AMS.substring(7, 5, ams);

		// Original unchanged
		await Assert.That(ams.ToPlainText()).IsEqualTo("Hello, World!");
		await Assert.That(ams.Length).IsEqualTo(13);

		// Substring is independent
		await Assert.That(sub.ToPlainText()).IsEqualTo("World");
	}

	[Test]
	public async Task Immutability_OptimizeDoesNotMutateOriginal()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var part1 = AMS.markupSingle(red, "Hello");
		var part2 = AMS.markupSingle(red, " World");
		var combined = AMS.concat(part1, part2);

		var optimized = AMS.optimize(combined);

		// Original combined still has 2 runs
		await Assert.That(combined.Runs.Length).IsEqualTo(2);

		// Optimized has 1 merged run
		await Assert.That(optimized.Runs.Length).IsEqualTo(1);

		// Both have the same text
		await Assert.That(combined.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
	}

	[Test]
	public async Task Immutability_RemoveDoesNotMutateOriginal()
	{
		var ams = AMS.single("Hello World");
		var removed = AMS.remove(ams, 5, 1);

		// Original unchanged
		await Assert.That(ams.ToPlainText()).IsEqualTo("Hello World");

		// Removed is independent
		await Assert.That(removed.ToPlainText()).IsEqualTo("HelloWorld");
	}

	// ── Sort-order invariant tests ────────────────────────────────
	// Runs must always be sorted ascending by Start position.
	// These tests verify the invariant is maintained across all operations.

	private static async Task AssertRunsSortedByStart(AMS.MarkupString ams)
	{
		for (int i = 1; i < ams.Runs.Length; i++)
		{
			await Assert.That(ams.Runs[i].Start)
				.IsGreaterThanOrEqualTo(ams.Runs[i - 1].Start);
		}
	}

	[Test]
	public async Task SortOrder_Single_RunStartsAtZero()
	{
		var ams = AMS.single("Hello");

		await Assert.That(ams.Runs.Length).IsEqualTo(1);
		await Assert.That(ams.Runs[0].Start).IsEqualTo(0);
	}

	[Test]
	public async Task SortOrder_Concat_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		var a = AMS.markupSingle(red, "Hello");
		var b = AMS.markupSingle(blue, " World");
		var result = AMS.concat(a, b);

		await Assert.That(result.Runs.Length).IsEqualTo(2);
		await AssertRunsSortedByStart(result);
		await Assert.That(result.Runs[0].Start).IsEqualTo(0);
		await Assert.That(result.Runs[1].Start).IsEqualTo(5);
	}

	[Test]
	public async Task SortOrder_MultipleConcat_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var green = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Green));

		var a = AMS.markupSingle(red, "AA");
		var b = AMS.markupSingle(blue, "BB");
		var c = AMS.markupSingle(green, "CC");
		var result = AMS.concat(AMS.concat(a, b), c);

		await Assert.That(result.Runs.Length).IsEqualTo(3);
		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_ConcatMany_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var green = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Green));

		var result = AMS.concatMany(new[]
		{
			AMS.markupSingle(red, "AA"),
			AMS.markupSingle(blue, "BB"),
			AMS.markupSingle(green, "CC")
		});

		await Assert.That(result.Runs.Length).IsEqualTo(3);
		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_Substring_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		var combined = AMS.concat(AMS.markupSingle(red, "Hello"), AMS.markupSingle(blue, " World"));
		var sub = AMS.substring(3, 5, combined);

		await AssertRunsSortedByStart(sub);
		await Assert.That(sub.Runs[0].Start).IsEqualTo(0);
	}

	[Test]
	public async Task SortOrder_Remove_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var green = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Green));

		var abc = AMS.concat(AMS.concat(AMS.markupSingle(red, "AA"), AMS.markupSingle(blue, "BB")), AMS.markupSingle(green, "CC"));
		var result = AMS.remove(abc, 2, 2);

		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_Replace_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		var original = AMS.markupSingle(red, "Hello World");
		var replacement = AMS.markupSingle(blue, "XX");
		var result = AMS.replace(original, replacement, 5, 1);

		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_InsertAt_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		var original = AMS.markupSingle(red, "HelloWorld");
		var insert = AMS.markupSingle(blue, " ");
		var result = AMS.insertAt(original, insert, 5);

		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_Split_AllSegmentsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		var combined = AMS.concat(AMS.markupSingle(red, "Hello"), AMS.markupSingle(blue, " World"));
		var parts = AMS.split(" ", combined);

		foreach (var part in parts)
		{
			await AssertRunsSortedByStart(part);
			if (part.Runs.Length > 0)
				await Assert.That(part.Runs[0].Start).IsEqualTo(0);
		}
	}

	[Test]
	public async Task SortOrder_Repeat_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));

		var ams = AMS.markupSingle(red, "AB");
		var result = AMS.repeat(ams, 5);

		await Assert.That(result.Runs.Length).IsEqualTo(5);
		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_FromMarkupString_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blue = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));

		var ms = global::MarkupString.TreeMarkupStringModule.markupSingle(red, "Hello ");
		var ms2 = global::MarkupString.TreeMarkupStringModule.markupSingle(blue, "World");
		var combined = global::MarkupString.TreeMarkupStringModule.concat(ms, ms2);
		var ams = AMS.fromMarkupString(combined);

		await AssertRunsSortedByStart(ams);
	}

	[Test]
	public async Task SortOrder_Optimize_RunsRemainSorted()
	{
		var red = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));

		var a = AMS.markupSingle(red, "Hello");
		var b = AMS.markupSingle(red, " World");
		var combined = AMS.concat(a, b);
		var optimized = AMS.optimize(combined);

		await AssertRunsSortedByStart(optimized);
	}
}
