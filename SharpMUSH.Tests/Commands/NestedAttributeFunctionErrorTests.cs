using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NestedAttributeFunctionErrorTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("direct", "syntax")]
	[Arguments("lambda", "syntax")]
	[Arguments("apply", "syntax")]
	[Arguments("lambda", "literal")]
	[Arguments("apply", "literal")]
	[Arguments("lambda", "success")]
	[Arguments("apply", "success")]
	[Arguments("u", "syntax")]
	[Arguments("ufun", "syntax")]
	[Arguments("localfun", "syntax")]
	[Arguments("global", "syntax")]
	[Arguments("u", "literal")]
	[Arguments("ufun", "literal")]
	[Arguments("localfun", "literal")]
	[Arguments("global", "literal")]
	[Arguments("u", "success")]
	[Arguments("ufun", "success")]
	[Arguments("localfun", "success")]
	[Arguments("global", "success")]
	public async Task NestedAttributeFunctionRetainsActualParserError(string kind, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var registry = Factory.Services.GetRequiredService<IUserDefinedFunctionService>();
		var actor = (await mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known();
		var owner = (await actor.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		var name = "nestederror" + Guid.NewGuid().ToString("N");
		var value = mode switch { "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "valid" };
		await attributes.SetAttributeAsync(actor, actor, name, MarkupText.Plain(value));
		var definition = new UserDefinedFunction(name, actor.Object().DBRef, name, 0, 0, true, null);
		if (kind == "global") registry.Define(definition);
		if (kind == "localfun") registry.DefineLocal(definition with { Owner = owner });
		try
		{
			var expression = kind switch
			{
				"direct" => value,
				"lambda" => "ulambda(#lambda/" + (mode == "syntax" ? "\\[" : value) + ")",
				"apply" => $"ulambda(#apply/ufun,me/{name})",
				"global" => name + "()",
				"localfun" => $"localfun({name})",
				_ => $"{kind}(me/{name})"
			};
			var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression));
			TestDiagnostics.WriteLine($"{kind}/{mode}: errors={result!.HadErrors}; message={result.Message}");
			await Assert.That(result.HadErrors).IsEqualTo(mode == "syntax");
			if (mode != "syntax") await Assert.That(result.Message!.ToPlainText()).IsEqualTo(value);
		}
		finally
		{
			if (kind == "global") registry.Delete(name);
			if (kind == "localfun") registry.Delete(name, owner);
		}
	}
}
