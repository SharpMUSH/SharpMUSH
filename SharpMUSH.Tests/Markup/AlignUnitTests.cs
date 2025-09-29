using Serilog;
using System.Text;
using MarkupString;
using SharpMUSH.Tests.Markup.Data;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Markup;

public class AlignUnitTests
{
	private static MString CallAlign(string widths, MString[] columns, MString filler, MString columnSeparator, MString rowSeparator)
	{
		var columnsList = Microsoft.FSharp.Collections.ListModule.OfArray(columns);
		return TextAligner.align(widths, columnsList, filler, columnSeparator, rowSeparator);
	}

	[Test]
	[MethodDataSource(typeof(Align), nameof(Align.AlignData))]
	public async Task AlignTest(AlignTestData data)
	{
		var (widths, columns, filler, columnSeparator, rowSeparator, expected) = data;

		var result = CallAlign(widths, columns, filler, columnSeparator, rowSeparator);

		Log.Logger.Information("Widths: {Widths}", widths);
		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", 
			result.ToPlainText(), Environment.NewLine, expected.ToPlainText());

		await Assert.That(result.ToPlainText()).IsEqualTo(expected.ToPlainText());
	}

	[Test]
	public async Task AlignWithInvalidParameters()
	{
		// Column count mismatch
		var result1 = CallAlign("10 10", [A.single("a")], A.single(" "), A.single(" "), A.single(Environment.NewLine));
		await Assert.That(result1.ToPlainText()).IsEqualTo("Column count mismatch");

		// Filler too long
		var result2 = CallAlign("10", [A.single("a")], A.single("--"), A.single(" "), A.single(Environment.NewLine));
		await Assert.That(result2.ToPlainText()).IsEqualTo("Filler is too long");
	}

	[Test]
	public async Task AlignWithExplicitNewlines()
	{
		// Test that explicit newlines in text are handled correctly
		var result = CallAlign("10", [A.single("line1\r\nline2\r\nline3")], A.single(" "), A.single(" "), A.single(Environment.NewLine));
		var expected = "line1     \r\nline2     \r\nline3     ";
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task AlignWithRepeatAndMixedContent()
	{
		// Test repeat columns with varying content lengths
		var result = CallAlign(
			"1. 20 1.",
			[A.single("|"), A.single("short\r\nmedium text\r\nvery long content here"), A.single("|")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine)
		);

		var lines = result.ToPlainText().Split(new[] { "\r\n" }, StringSplitOptions.None);

		// All lines should start and end with |
		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith("|")).IsTrue();
			await Assert.That(line.EndsWith("|")).IsTrue();
		}
	}

	[Test]
	public async Task AlignWithCustomSeparators()
	{
		// Test with various custom separators
		var result = CallAlign(
			"5 5 5",
			[A.single("A"), A.single("B"), A.single("C")],
			A.single("."),
			A.single("||"),
			A.single(" // ")
		);

		var expected = "A....||B....||C....";
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task AlignWithNoColSepOption()
	{
		// Test that # option prevents column separator
		var result = CallAlign(
			"5# 5 5",
			[A.single("A"), A.single("B"), A.single("C")],
			A.single(" "),
			A.single("|"),
			A.single(Environment.NewLine)
		);

		// First column should not have separator after it
		var expected = "A    B    |C    ";
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task AlignWithFullJustification()
	{
		// Test full justification spreads words evenly
		var result = CallAlign(
			"_25",
			[A.single("one two three four")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine)
		);

		var resultText = result.ToPlainText();

		// Should be exactly 25 characters
		await Assert.That(resultText.Length).IsEqualTo(25);

		// Should contain all words
		await Assert.That(resultText.Contains("one")).IsTrue();
		await Assert.That(resultText.Contains("two")).IsTrue();
		await Assert.That(resultText.Contains("three")).IsTrue();
		await Assert.That(resultText.Contains("four")).IsTrue();
	}
}
