using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class SearchPredicateResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("@search", "syntax")]
	[Arguments("@search", "literal")]
	[Arguments("@search", "true")]
	[Arguments("@search", "false")]
	[Arguments("lsearch", "syntax")]
	[Arguments("lsearchr", "syntax")]
	[Arguments("lsearch", "literal")]
	[Arguments("lsearchr", "literal")]
	[Arguments("lsearch", "true")]
	[Arguments("lsearchr", "true")]
	[Arguments("lsearch", "false")]
	[Arguments("lsearchr", "false")]
	public async Task PredicateFailureRetainsMetadataWithoutChangingMatching(string function, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var id = await TestIsolationHelpers.CreateTestPlayerAsync(Factory.Services, mediator, "SearchResult");
		var actor = (await mediator.Send(new GetObjectNodeQuery(id))).Known;
		var value = mode switch { "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", "true" => "1", _ => "0" };
		await attributes.SetAttributeAsync(actor, actor, "BODY", MarkupText.Plain(value));
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(id));
		var expression = $"{function}(all,mindb,{id.Number},maxdb,{id.Number},eval,lit(ufun({id}/BODY)))";
		var result = function == "@search"
			? await parser.CommandListParse(MarkupText.Plain($"@search all mindb={id.Number},maxdb={id.Number},eval=ufun({id}/BODY)"))
			: await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.HadErrors).IsEqualTo(mode == "syntax");
		await Assert.That(result.Message!.Text).IsEqualTo(function == "@search" ? (mode == "true" ? "1" : "0") : mode == "true" ? id.ToString() : "");
	}
}
