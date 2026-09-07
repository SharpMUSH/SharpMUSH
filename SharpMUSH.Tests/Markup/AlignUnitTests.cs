using Serilog;
using SharpMUSH.Library.Markup;
using SharpMUSH.Tests.Markup.Data;

namespace SharpMUSH.Tests.Markup;

public class AlignUnitTests
{
	private static MString CallAlign(string widths, MString[] columns, MString filler, MString columnSeparator, MString rowSeparator)
	{
		return TextAligner.Align(widths, columns, filler, columnSeparator, rowSeparator);
	}

	[Test]
	[MethodDataSource(typeof(Align), nameof(Align.AlignData))]
	public async Task AlignTest(AlignTestData data)
	{
		var (widths, columns, filler, columnSeparator, rowSeparator, expected) = data;

		var result = CallAlign(widths, columns, filler, columnSeparator, rowSeparator);

		Log.Logger.Information("Widths: {Widths}", widths);
		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}",
			result.ToPlainText(), "\n", expected.ToPlainText());

		await Assert.That(result.ToPlainText()).IsEqualTo(expected.ToPlainText());
	}

	[Test]
	public async Task AlignWithInvalidParameters()
	{
		var result1 = CallAlign("10 10", [MarkupText.Plain("a")], MarkupText.Space, MarkupText.Space, MarkupText.Plain("\n"));
		await Assert.That(result1.ToPlainText()).IsEqualTo("#-1 COLUMN COUNT MISMATCH");

		var result2 = CallAlign("10", [MarkupText.Plain("a")], MarkupText.Plain("--"), MarkupText.Space, MarkupText.Plain("\n"));
		await Assert.That(result2.ToPlainText()).IsEqualTo("#-1 FILLER MUST BE ONE CHARACTER");
	}

	[Test]
	public async Task AlignWithExplicitNewlines()
	{
		var result = CallAlign("10", [MarkupText.Plain("line1\nline2\nline3")], MarkupText.Space, MarkupText.Space, MarkupText.Plain("\n"));
		var expected = "line1     \nline2     \nline3     ";
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task AlignWithRepeatAndMixedContent()
	{
		var result = CallAlign(
			"1. 20 1.",
			[MarkupText.Plain("|"), MarkupText.Plain("short\nmedium text\nvery long content here"), MarkupText.Plain("|")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n")
		);

		var lines = result.ToPlainText().Split(["\n"], StringSplitOptions.None);

		foreach (var line in lines)
		{
			await Assert.That(line).StartsWith("|");
			await Assert.That(line).EndsWith("|");
		}
	}

	[Test]
	public async Task AlignWithCustomSeparators()
	{
		var result = CallAlign(
			"5 5 5",
			[MarkupText.Plain("A"), MarkupText.Plain("B"), MarkupText.Plain("C")],
			MarkupText.Plain("."),
			MarkupText.Plain("||"),
			MarkupText.Plain(" // ")
		);

		var expected = "A....||B....||C....";
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task AlignWithNoColSepOption()
	{
		var result = CallAlign(
			"5# 5 5",
			[MarkupText.Plain("A"), MarkupText.Plain("B"), MarkupText.Plain("C")],
			MarkupText.Space,
			MarkupText.Plain("|"),
			MarkupText.Plain("\n")
		);

		var expected = "A    B    |C    ";
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task AlignWithFullJustification()
	{
		var result = CallAlign(
			"_25",
			[MarkupText.Plain("one two three four")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n")
		);

		var resultText = result.ToPlainText();

		await Assert.That(resultText.Length).IsEqualTo(25);

		await Assert.That(resultText.Contains("one")).IsTrue();
		await Assert.That(resultText.Contains("two")).IsTrue();
		await Assert.That(resultText.Contains("three")).IsTrue();
		await Assert.That(resultText.Contains("four")).IsTrue();
	}

	/// <summary>
	/// Columns are measured in terminal cells, not code units: a CJK character occupies two, so five
	/// of them fill a ten-wide column exactly and nothing is padded after them.
	/// </summary>
	[Test]
	public async Task AlignMeasuresWideCharactersInDisplayCells()
	{
		var result = CallAlign("10 4", [MarkupText.Plain("\u4f60\u597d\u4e16\u754c\u3002"), MarkupText.Plain("ok")],
			MarkupText.Space, MarkupText.Space, MarkupText.Plain("\n"));

		await Assert.That(result.ToPlainText()).IsEqualTo("\u4f60\u597d\u4e16\u754c\u3002 ok  ");
	}
}
