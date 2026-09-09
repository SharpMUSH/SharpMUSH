using MarkupString;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class PrintfFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMUSHCodeParser Parser => Factory.FunctionParser;

	[Test]
	[Arguments("printf(lit(%s:%+05d:%.2f),界,12,2.345)", "界:+0012:2.34")]
	[Arguments("printf(%%s,hello)", "hello")]
	[Arguments("printf(%%%%)", "%")]
	[Arguments("printf(lit(%%))", "%")]
	[Arguments("printf()", "")]
	[Arguments("printf(lit(%s))", PrintfFormatter.ArgumentCountMismatch)]
	[Arguments("printf(text,extra)", PrintfFormatter.ArgumentCountMismatch)]
	public async Task ParserEscapingAndArgumentContracts(string expression, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.Text).IsEqualTo(expected);
	}

	[Test]
	public async Task MixedColorCjkAndEmojiFieldsAlign()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("printf(lit(%4s|%-4s),ansi(r,界),ansi(b,😀))")))!.Message!;
		var expected = (await Parser.FunctionParse(MarkupText.Plain("strcat(%b%b,ansi(r,界),|,ansi(b,😀),%b%b)")))!.Message!;
		await Assert.That(result.Text).IsEqualTo("  界|😀  ");
		await Assert.That(result.Runs.SequenceEqual(expected.Runs)).IsTrue();
	}

	[Test]
	public async Task OutputCeilingStopsSurroundingEvaluation()
	{
		var format = string.Concat(Enumerable.Repeat("%65536s", 81));
		var values = string.Join(',', Enumerable.Repeat("x", 81));
		var expression = $"cat(printf(lit({format}),{values}),unexpected)";
		var result = (await Parser.FunctionParse(MarkupText.Plain(expression)))!.Message!;
		await Assert.That(result.Text).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
	}

	[Test]
	public async Task HelpTopicIsIndexed()
	{
		var help = await Factory.Services.GetRequiredService<ITextFileService>().GetEntryAsync("help/sharpfunc", "PRINTF()");
		await Assert.That(help).IsNotNull();
		await Assert.That(help!).Contains("printf(");
	}
}
