using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class MessageFormatResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(false, false, "syntax")]
	[Arguments(false, true, "syntax")]
	[Arguments(true, false, "syntax")]
	[Arguments(true, true, "syntax")]
	[Arguments(false, false, "literal")]
	[Arguments(false, true, "literal")]
	[Arguments(true, false, "literal")]
	[Arguments(true, true, "literal")]
	[Arguments(false, false, "success")]
	[Arguments(false, true, "success")]
	[Arguments(true, false, "success")]
	[Arguments(true, true, "success")]
	[Arguments(false, false, "missing")]
	[Arguments(false, true, "missing")]
	[Arguments(true, false, "missing")]
	[Arguments(true, true, "missing")]
	public async Task MessageRetainsActualFormatFailure(bool command, bool pinned, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var actor = (await mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known();
		var owner = await actor.Object().Owner.WithCancellation(CancellationToken.None);
		var name = "messageformat" + Guid.NewGuid().ToString("N");
		var room = (await mediator.Send(new GetObjectNodeQuery(await mediator.Send(new CreateRoomCommand(name, owner))))).AsRoom;
		var target = (await mediator.Send(new GetObjectNodeQuery(await mediator.Send(new CreateThingCommand(name, room, owner, room))))).Known();
		var value = mode switch { "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "valid" };
		if (mode != "missing") await attributes.SetAttributeAsync(actor, target, name, MarkupText.Plain(value));
		var reference = target.Object().DBRef;
		var format = pinned ? $"{reference}/{name}" : name;
		var result = command
			? await Factory.CommandParser.CommandListParse(MarkupText.Plain($"@message/silent {reference}=fallback,{format}"))
			: await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"message({reference},fallback,{format})"));
		Console.WriteLine($"command={command}, pinned={pinned}, mode={mode}: errors={result!.HadErrors}, output={result.Message}");
		await Assert.That(result.HadErrors).IsEqualTo(mode == "syntax");
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo("");
		var delivered = Factory.Notifications.For(reference);
		await Assert.That(delivered.Count).IsEqualTo(1);
		if (mode == "syntax") await Assert.That(delivered.Single()).Contains("#-1 PARSER FAILURE");
		else await Assert.That(delivered.Single()).IsEqualTo(mode == "missing" ? "fallback" : value);
	}
}
