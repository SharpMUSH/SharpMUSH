using System.Globalization;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Functions;

public class PrintfFormatterTests
{
	[Test]
	[Arguments("%s", "界", "界")]
	[Arguments("%5s", "界", "   界")]
	[Arguments("%-5s", "界", "界   ")]
	[Arguments("%.1s", "界a", "")]
	[Arguments("%.2s", "界a", "界")]
	[Arguments("%.1s", "éx", "é")]
	[Arguments("%.2s", "👩‍👩‍👧‍👦x", "👩‍👩‍👧‍👦")]
	[Arguments("%.0s", "text", "")]
	[Arguments("%4s", "a\nb", "  a\nb")]
	[Arguments("%+06d", "12", "+00012")]
	[Arguments("%06d", "-12", "-00012")]
	[Arguments("%-06d", "-12", "-12   ")]
	[Arguments("%.4d", "-12", "-0012")]
	[Arguments("%d", "-9223372036854775808", "-9223372036854775808")]
	[Arguments("%.2f", "2.345", "2.34")]
	[Arguments("%+.2f", "2.355", "+2.36")]
	[Arguments("%08.2f", "-2.5", "-0002.50")]
	public async Task FormatsExplicitGrammar(string format, string argument, string expected)
	{
		var success = PrintfFormatter.TryFormat(MarkupText.Plain(format), [MarkupText.Plain(argument)], out var result, out var error);
		await Assert.That(success).IsTrue();
		await Assert.That(error).IsNull();
		await Assert.That(result.Text).IsEqualTo(expected);
	}

	[Test]
	[Arguments("%q", PrintfFormatter.InvalidFormat)]
	[Arguments("%", PrintfFormatter.InvalidFormat)]
	[Arguments("%.s", PrintfFormatter.InvalidFormat)]
	[Arguments("%+s", PrintfFormatter.InvalidFormat)]
	[Arguments("%00d", PrintfFormatter.InvalidFormat)]
	[Arguments("%65537s", PrintfFormatter.FieldLimitExceeded)]
	[Arguments("%.65537s", PrintfFormatter.FieldLimitExceeded)]
	[Arguments("%.29f", PrintfFormatter.FieldLimitExceeded)]
	[Arguments("%99999999999999999999999d", PrintfFormatter.FieldLimitExceeded)]
	[Arguments("%ś", PrintfFormatter.InvalidFormat)]
	public async Task RejectsInvalidOrExcessiveFields(string format, string expectedError)
	{
		var success = PrintfFormatter.TryFormat(MarkupText.Plain(format), [MarkupText.Plain("1")], out _, out var error);
		await Assert.That(success).IsFalse();
		await Assert.That(error).IsEqualTo(expectedError);
	}

	[Test]
	public async Task RejectsMissingExtraAndNonInvariantNumbers()
	{
		foreach (var values in new MarkupText[][] { [], [MarkupText.Plain("a"), MarkupText.Plain("b")] })
		{
			await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain("%s"), values, out _, out var error)).IsFalse();
			await Assert.That(error).IsEqualTo(PrintfFormatter.ArgumentCountMismatch);
		}
		foreach (var text in new[] { "1,5", " 1", "1 ", "1e2", "text", "1\0", "1\0\0" })
		{
			await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain("%f"), [MarkupText.Plain(text)], out _, out var error)).IsFalse();
			await Assert.That(error).IsEqualTo(ErrorMessages.Returns.Numbers);
		}
	}

	[Test]
	[Arguments("%d", "9223372036854775808")]
	[Arguments("%d", "1.5")]
	[Arguments("%d", "1\0")]
	[Arguments("%f", "79228162514264337593543950336")]
	[Arguments("%f", "NaN")]
	public async Task RejectsNumbersOutsideTheSupportedDomain(string format, string value)
	{
		await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain(format), [MarkupText.Plain(value)],
			out _, out var error)).IsFalse();
		await Assert.That(error).IsEqualTo(ErrorMessages.Returns.Numbers);
	}

	[Test]
	public async Task FormattingDoesNotDependOnCurrentCulture()
	{
		var original = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
			await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain("%+.2f"), [MarkupText.Plain("1234.5")],
				out var result, out _)).IsTrue();
			await Assert.That(result.Text).IsEqualTo("+1234.50");
		}
		finally { CultureInfo.CurrentCulture = original; }
	}

	[Test]
	public async Task FormatAndDirectiveCountsAreBounded()
	{
		foreach (var format in new[]
		{
			new string('x', PrintfFormatter.MaxFormatCodeUnits + 1),
			string.Concat(Enumerable.Repeat("%%", 1025)),
			string.Concat(Enumerable.Repeat("%s", PrintfFormatter.MaxFields + 1))
		})
		{
			await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain(format), [], out _, out var error)).IsFalse();
			await Assert.That(error).IsEqualTo(PrintfFormatter.FieldLimitExceeded);
		}
		await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain(string.Concat(Enumerable.Repeat("%%", 1024))),
			[], out var result, out _)).IsTrue();
		await Assert.That(result.Text).IsEqualTo(new string('%', 1024));
	}

	[Test]
	public async Task PreservesStringAndNumericMarkupWithoutRendering()
	{
		var source = MarkupText.Wrap(HtmlMarkup.Create("b"),
			MarkupText.Wrap(AnsiMarkup.Create(underlined: true), "é😀"));
		await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain("%.1s"), [source], out var result, out _)).IsTrue();
		var expected = source.SubstringGraphemes(0, 1);
		await Assert.That(result.Runs.SequenceEqual(expected.Runs)).IsTrue();
		var number = MarkupText.Wrap(HtmlMarkup.Create("i"), "12");
		await Assert.That(PrintfFormatter.TryFormat(MarkupText.Plain("%04d"), [number], out result, out _)).IsTrue();
		await Assert.That(result.Runs.SequenceEqual(MarkupText.Wrap(HtmlMarkup.Create("i"), "0012").Runs)).IsTrue();
	}

	[Test]
	public async Task TemplateStyleWrapsTheFieldAndLongClustersStayWhole()
	{
		var cluster = "e" + new string('\u0301', 1024);
		var source = MarkupText.Wrap(HtmlMarkup.Create("b"), cluster);
		var style = AnsiMarkup.Create(underlined: true);
		var format = MarkupText.Wrap(style, "%1.1s");
		await Assert.That(PrintfFormatter.TryFormat(format, [source], out var result, out _)).IsTrue();
		var expected = MarkupText.Wrap(style, source);
		await Assert.That(result.Text).IsEqualTo(cluster);
		await Assert.That(result.Runs.SequenceEqual(expected.Runs)).IsTrue();
	}

	[Test]
	public async Task OutputBudgetIsCheckedBeforeConcatenatingLargeArguments()
	{
		var input = MarkupText.Plain(new string('x', FunctionLimits.MaxOutputCodeUnits));
		var format = MarkupText.Plain("%s%s");
		var values = new[] { input, input };
		var before = GC.GetAllocatedBytesForCurrentThread();
		var success = PrintfFormatter.TryFormat(format, values, out _, out var error);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		await Assert.That(success).IsFalse();
		await Assert.That(error).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
		await Assert.That(allocated).IsLessThan(100_000L);
	}
}
