using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ArgumentWrapperResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("single", "syntax")]
	[Arguments("single", "literal")]
	[Arguments("single", "success")]
	[Arguments("single", "noparse")]
	[Arguments("eq-single", "syntax")]
	[Arguments("eq-single", "literal")]
	[Arguments("eq-single", "success")]
	[Arguments("eq-single", "noparse")]
	[Arguments("eq-left", "syntax")]
	[Arguments("eq-left", "literal")]
	[Arguments("eq-left", "success")]
	[Arguments("eq-left", "noparse")]
	[Arguments("eq-right", "syntax")]
	[Arguments("eq-right", "literal")]
	[Arguments("eq-right", "success")]
	[Arguments("eq-right", "noparse")]
	[Arguments("args-left", "syntax")]
	[Arguments("args-left", "literal")]
	[Arguments("args-left", "success")]
	[Arguments("args-left", "noparse")]
	[Arguments("args-right", "syntax")]
	[Arguments("args-right", "literal")]
	[Arguments("args-right", "success")]
	[Arguments("args-right", "noparse")]
	[Arguments("comma", "syntax")]
	[Arguments("comma", "literal")]
	[Arguments("comma", "success")]
	[Arguments("comma", "noparse")]
	public async Task EvaluatingPublicEntryRetainsChildFailure(string entry, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var id = await TestIsolationHelpers.CreateTestPlayerAsync(Factory.Services, mediator, "ArgWrapper");
		var actor = (await mediator.Send(new GetObjectNodeQuery(id))).Expect<AnySharpObject>();
		var value = mode switch { "syntax" or "noparse" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "ok" };
		await attributes.SetAttributeAsync(actor, actor, "BODY", MarkupText.Plain(value));
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(id) with
		{ ParseMode = mode == "noparse" ? ParseMode.NoParse : ParseMode.Default });
		var call = $"ufun({id}/BODY)";
		var text = entry switch
		{
			"eq-left" or "args-left" => call + "=right",
			"eq-right" => "left=" + call,
			"args-right" => "left=," + call + ",tail",
			"comma" => "," + call + ",tail",
			_ => call
		};
		var result = entry switch
		{
			"single" => await parser.CommandSingleArgParse(MarkupText.Plain(text)),
			"args-left" or "args-right" => await parser.CommandEqSplitArgsParse(MarkupText.Plain(text)),
			"comma" => await parser.CommandCommaArgsParse(MarkupText.Plain(text)),
			_ => await parser.CommandEqSplitParse(MarkupText.Plain(text))
		};
		await Assert.That(result!.HadErrors).IsEqualTo(mode == "syntax");
		if (mode != "syntax")
		{
			var expectedValue = mode == "noparse" ? call : value;
			var expected = entry switch
			{
				"eq-left" or "args-left" => expectedValue + "|right",
				"eq-right" => "left|" + expectedValue,
				"args-right" => "left||" + expectedValue + "|tail",
				"comma" => "|" + expectedValue + "|tail",
				_ => expectedValue
			};
			await Assert.That(string.Join("|", result.Arguments!.Select(x => x.Text))).IsEqualTo(expected);
		}
	}
}
