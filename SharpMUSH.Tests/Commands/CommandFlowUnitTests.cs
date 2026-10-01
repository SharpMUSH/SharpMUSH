using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommandFlowUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Arguments("@ifelse 1=@pemit me=1 True,@pemit me=1 False", "1 True")]
	[Arguments("@ifelse 0=@pemit me=2 True,@pemit me=2 False", "2 False")]
	[Arguments("@ifelse 1=@pemit me=3 True", "3 True")]
	[Arguments("@ifelse 1={@pemit me=4 True},{@pemit me=4 False}", "4 True")]
	[Arguments("@ifelse 0={@pemit me=5 True},{@pemit me=5 False}", "5 False")]
	[Arguments("@ifelse 1={@pemit me=6 True}", "6 True")]
	public async ValueTask IfElse(string str, string expected)
	{
		// A player of its own: the expected messages are short enough for another test to send #1.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CmdFlowIfElse");
		TestDiagnostics.WriteLine("Testing: {0}", str);
		await WebAppFactoryArg.CommandParserFor(testPlayer.DbRef, testPlayer.Handle).CommandListParse(MarkupText.Plain(str));

		var delivered = WebAppFactoryArg.Notifications.DeliveriesFor(testPlayer.DbRef).Count(delivery =>
			delivery.Message == expected
			&& delivery.Sender == testPlayer.DbRef
			&& delivery.Type == INotifyService.NotificationType.PrivateEmit);
		await Assert.That(delivered).IsEqualTo(1);
	}

	[Test, Skip("Command is failing. Needs to be implemented correctly.")]
	public async ValueTask Retry()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CmdFlowRetry");
		var testParser = WebAppFactoryArg.CommandParserFor(testPlayer.DbRef, testPlayer.Handle);
		await testParser.CommandListParse(MarkupText.Plain("think Retry %0; @retry gt(%0,-1)=dec(%0)"));

		await NotifyService.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessageEquals(msg, "Retry ")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessageEquals(msg, "Retry -1")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}
}
