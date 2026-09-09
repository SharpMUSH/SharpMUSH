using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class UnicodeFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMUSHCodeParser Parser => Factory.FunctionParser;

	[Test]
	[Arguments("displaywidth(界)", "2")]
	[Arguments("graphemecount(界)", "1")]
	[Arguments("strlen(界)", "2")]
	[Arguments("displaywidth(é😀)", "3")]
	[Arguments("graphemecount(é😀)", "2")]
	[Arguments("graphemecount(👍🏽👩‍👩‍👧‍👦🇺🇸)", "3")]
	[Arguments("graphemes(👍🏽👩‍👩‍👧‍👦🇺🇸,|)", "👍🏽|👩‍👩‍👧‍👦|🇺🇸")]
	[Arguments("graphemes(é界)", "é 界")]
	[Arguments("graphemes(é界,--)", "é--界")]
	[Arguments("graphemes(é界,)", "é界")]
	[Arguments("graphemes()", "")]
	[Arguments("graphemecount()", "0")]
	[Arguments("displaywidth()", "0")]
	[Arguments("flip(é😀界)", "界😀é")]
	[Arguments("graphemes(repeat(x,4096),repeat(y,2048))", ErrorMessages.Returns.OutputTooLarge)]
	[Arguments("cat(graphemes(repeat(x,4096),repeat(y,2048)),unexpected)", ErrorMessages.Returns.OutputTooLarge)]
	public async Task UnicodeContracts(string expression, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.Text).IsEqualTo(expected);
	}

	[Test]
	public async Task EmptySeparatorPreservesAnsiAndHtmlCoverage()
	{
		var input = MarkupText.Concat(
			MarkupText.Wrap(AnsiMarkup.Create(underlined: true), "é"),
			MarkupText.Wrap(HtmlMarkup.Create("b"), "😀界"));
		var expression = MarkupText.Concat([MarkupText.Plain("graphemes("), input, MarkupText.Plain(",)")]);
		var result = (await Parser.FunctionParse(expression))!.Message!;
		await Assert.That(result.Text).IsEqualTo(input.Text);
		await Assert.That(result.Runs.SequenceEqual(input.Runs)).IsTrue();
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo(input.Render(MarkupFormat.Ansi));
		await Assert.That(result.Render(MarkupFormat.Html)).IsEqualTo(input.Render(MarkupFormat.Html));
	}

	[Test]
	public async Task MarkedSeparatorAndClustersKeepTheirLayers()
	{
		var input = MarkupText.Wrap(AnsiMarkup.Create(underlined: true),
			MarkupText.Wrap(HtmlMarkup.Create("b"), "é😀界"));
		var separator = MarkupText.Wrap(HtmlMarkup.Create("i"), "|");
		var expression = MarkupText.Concat([MarkupText.Plain("graphemes("), input,
			MarkupText.Plain(","), separator, MarkupText.Plain(")")]);
		var result = (await Parser.FunctionParse(expression))!.Message!;
		var expected = MarkupText.Join(separator, input.EnumerateGraphemes());
		await Assert.That(result.Text).IsEqualTo("é|😀|界");
		await Assert.That(result.Runs.SequenceEqual(expected.Runs)).IsTrue();
	}

	[Test]
	[Arguments("DISPLAYWIDTH()")]
	[Arguments("GRAPHEMECOUNT()")]
	[Arguments("GRAPHEMES()")]
	public async Task UnicodeHelpTopicsAreIndexed(string topic)
	{
		var service = Factory.Services.GetRequiredService<ITextFileService>();
		var body = await service.GetEntryAsync("help/sharpfunc", topic);
		await Assert.That(body).IsNotNull();
		await Assert.That(body!).Contains(topic.Split('(')[0].ToLowerInvariant() + "(");
	}

	[Test]
	public async Task LongClusterRemainsWholeWhenSplittingAndFlipping()
	{
		var cluster = "e" + new string('\u0301', 1024);
		var split = await Parser.FunctionParse(MarkupText.Plain($"graphemes({cluster}界,|)"));
		var flip = await Parser.FunctionParse(MarkupText.Plain($"flip({cluster}界)"));
		await Assert.That(split!.Message!.Text).IsEqualTo(cluster + "|界");
		await Assert.That(flip!.Message!.Text).IsEqualTo("界" + cluster);
	}
}
