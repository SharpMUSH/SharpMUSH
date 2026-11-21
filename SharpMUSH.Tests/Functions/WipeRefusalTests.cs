using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// PennMUSH's <c>fun_wipe</c> calls <c>do_wipe</c>, so <c>wipe()</c> on an object the caller does not control
/// tells it why, as <c>@wipe</c> does, and leaves the attributes alone.
/// </summary>
public class WipeRefusalTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	[Test]
	public async ValueTask WipeOnAnObjectNotControlledSaysWhy()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("WipeRefusalRoom"), god));
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WipeRefusal", room);

		var attribute = TestIsolationHelpers.GenerateUniqueName("WIPEKEEP").ToUpperInvariant();
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} me=keep"));

		var parser = WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle);
		var before = Notifications.For(mortal.DbRef).Count;
		var result = (await parser.EvaluateAsync(MarkupText.Plain($"wipe(#1/{attribute})"))).ToPlainText();
		var heard = Notifications.For(mortal.DbRef).Skip(before).ToList();

		await Assert.That(result).IsEqualTo("#-1 PERMISSION DENIED: NOT CONTROLLED");
		await Assert.That(heard).Count().IsEqualTo(1);
		await Assert.That(heard[0]).StartsWith("Permission denied.");
		await Assert.That((await WebAppFactoryArg.CommandParser.EvaluateAsync(MarkupText.Plain($"get(#1/{attribute})"))).ToPlainText())
			.IsEqualTo("keep");
	}
}
