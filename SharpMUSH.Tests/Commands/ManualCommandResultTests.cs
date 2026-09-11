using Mediator;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ManualCommandResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("attribute", "throw")]
	[Arguments("edit", "throw")]
	[Arguments("attribute", "syntax")]
	[Arguments("attribute", "literal")]
	[Arguments("attribute", "success")]
	[Arguments("edit", "syntax")]
	[Arguments("edit", "literal")]
	[Arguments("edit", "success")]
	[Arguments("all", "syntax")]
	[Arguments("all", "literal")]
	[Arguments("all", "success")]
	[Arguments("check", "syntax")]
	[Arguments("check", "success")]
	[Arguments("invalid", "syntax")]
	[Arguments("unmatched", "syntax")]
	public async Task ManualEvaluationRetainsOnlyActualParserFailures(string kind, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var id = await TestIsolationHelpers.CreateTestPlayerAsync(Factory.Services, mediator, "ManualResult");
		var actor = (await mediator.Send(new GetObjectNodeQuery(id))).Expect<AnySharpObject>();
		var calls = 0;
		var value = mode switch { "throw" => "manualfailure()", "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "OUTPUT" };
		await attributes.SetAttributeAsync(actor, actor, "BODY", MarkupText.Plain(value));
		await attributes.SetAttributeAsync(actor, actor, "TARGET", MarkupText.Plain("xx"));
		var original = (MUSHCodeParser)Factory.Services.GetRequiredService<IMUSHCodeParser>();
		var functions = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) functions.Add(pair.Key, pair.Value);
		functions.Add("manualfailure", (new FunctionDefinition(new SharpFunctionAttribute
		{ Name = "manualfailure", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 0 },
		_ => { calls++; throw new InvalidOperationException("manual evaluation failed"); }), true));
		var parser = new MUSHCodeParser(original.Logger, functions, original.CommandLibrary, original.Configuration, Factory.Services)
			.FromState(ParserState.RootFor(id));
		var command = kind == "attribute"
			? $"&ufun({id}/BODY)OUTPUT {id}=stored"
			: $"@edit/regexp{(kind is "all" or "check" ? "/" + kind : "")} {id}/TARGET={(kind == "invalid" ? "*" : kind == "unmatched" ? "z" : "x")},ufun({id}/BODY)";
		var result = await parser.CommandListParse(MarkupText.Plain(command));
		await Assert.That(result!.HadErrors).IsEqualTo((mode is "syntax" or "throw") && kind is not ("invalid" or "unmatched"));
		if (kind != "attribute" && mode is not ("syntax" or "throw") || kind is "invalid" or "unmatched" or "check")
		{
			var attr = (await attributes.GetAttributeAsync(actor, actor, "TARGET", IAttributeService.AttributeMode.Read)).Expect<SharpAttribute[]>();
			var expected = kind is "invalid" or "unmatched" or "check" ? "xx" : kind == "all" ? value + value : value + "x";
			await Assert.That(attr.Last().Value.Text).IsEqualTo(expected);
		}
		if (kind == "attribute" && mode == "success")
		{
			var attr = (await attributes.GetAttributeAsync(actor, actor, "OUTPUTOUTPUT", IAttributeService.AttributeMode.Read)).Expect<SharpAttribute[]>();
			await Assert.That(attr.Last().Value.Text).IsEqualTo("stored");
		}
		if (mode == "throw") await Assert.That(calls).IsEqualTo(1);
	}
}
