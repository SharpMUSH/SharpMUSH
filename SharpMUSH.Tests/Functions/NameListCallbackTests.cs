using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class NameListCallbackTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private async Task<DBRef> CallbackObject(string body)
	{
		var connection = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var created = await WebAppFactoryArg.CommandParser.CommandParse(1, connection,
			MarkupText.Plain($"@create NameListCallback_{Guid.NewGuid():N}"));
		var dbref = DBRef.Parse(created.Message!.ToPlainText());
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var obj = (await mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var attributes = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		await attributes.SetAttributeAsync(god, obj, "CALLBACK", MarkupText.Plain(body.Replace("$OBJECT", dbref.ToString())));
		return dbref;
	}

	[Test]
	public async Task CallbackEvaluatesExpressionWithEnvironmentArguments()
	{
		var obj = await CallbackObject("setq(namelist_callback,strcat(%0,|,%1))");
		var missing = $"Missing_{Guid.NewGuid():N}";
		var result = await WebAppFactoryArg.FunctionParser.FunctionParse(
			MarkupText.Plain($"[namelist({missing},{obj}/CALLBACK)]|[r(namelist_callback)]"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo($"#-1|{missing}|#-1");
	}

	[Test]
	public async Task CallbackDoesNotExecuteCommandText()
	{
		var obj = await CallbackObject("&COMMAND_EXECUTED $OBJECT=yes");
		await WebAppFactoryArg.FunctionParser.FunctionParse(
			MarkupText.Plain($"namelist(Missing_{Guid.NewGuid():N},{obj}/CALLBACK)"));
		var result = await WebAppFactoryArg.FunctionParser.FunctionParse(
			MarkupText.Plain($"hasattr({obj},COMMAND_EXECUTED)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("0");
	}
}
