using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommandArgumentResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("@ifelse {0}={think yes},{think no}", "syntax")]
	[Arguments("@ifelse {0}={think yes},{think no}", "literal")]
	[Arguments("@ifelse {0}={think yes},{think no}", "success")]
	[Arguments("@emit {0}", "syntax")]
	[Arguments("@emit {0}", "literal")]
	[Arguments("@emit {0}", "success")]
	public async Task EagerArgumentsPreserveFailureAcrossCommandDispatch(string command, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var id = await TestIsolationHelpers.CreateTestPlayerAsync(Factory.Services, mediator, "CommandResult");
		var actor = (await mediator.Send(new GetObjectNodeQuery(id))).Expect<AnySharpObject>();
		await attributes.SetAttributeAsync(actor, actor, "BODY", MarkupText.Plain(mode switch
		{
			"syntax" => "[",
			"literal" => "#-1 EXCEPTION: ordinary text",
			_ => "ok"
		}));
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(id));
		var result = await parser.CommandListParse(MarkupText.Plain(command.Replace("{0}", $"[ufun({id}/BODY)]")));
		await Assert.That(result!.HadErrors).IsEqualTo(mode == "syntax");
	}
}
