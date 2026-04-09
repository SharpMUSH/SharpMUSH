using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
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

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	private IMUSHCodeParser ParserForPlayer(TestIsolationHelpers.TestPlayer player)
	{
		var parser = Parser;
		return parser.FromState(parser.CurrentState with
		{
			Executor = player.DbRef,
			Enactor = player.DbRef,
			Caller = player.DbRef,
			Handle = player.Handle
		});
	}

	[Test]
	[NotInParallel]
	[Arguments("@ifelse 1=@pemit #1=1 True,@pemit #1=1 False", "1 True")]
	[Arguments("@ifelse 0=@pemit #1=2 True,@pemit #1=2 False", "2 False")]
	[Arguments("@ifelse 1=@pemit #1=3 True", "3 True")]
	[Arguments("@ifelse 1={@pemit #1=4 True},{@pemit #1=4 False}", "4 True")]
	[Arguments("@ifelse 0={@pemit #1=5 True},{@pemit #1=5 False}", "5 False")]
	[Arguments("@ifelse 1={@pemit #1=6 True}", "6 True")]
	public async ValueTask IfElse(string str, string expected)
	{
		var testPlayer = await CreateTestPlayerAsync("IfElse");
		var executor = testPlayer.DbRef;
		var cmd = str.Replace("#1", $"{testPlayer.DbRef}");
		Console.WriteLine("Testing: {0}", cmd);
		await ParserForPlayer(testPlayer).CommandListParse(MModule.single(cmd));

		await NotifyService.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == expected) ||
				(msg.IsT1 && msg.AsT1 == expected)), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Retry()
	{
		var testPlayer = await CreateTestPlayerAsync("Retry");
		var executor = testPlayer.DbRef;
		await ParserForPlayer(testPlayer).CommandListParse(MModule.single("think %0; @retry gt(%0,-1)=dec(%0)"));

		await NotifyService.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		await NotifyService.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "-1")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
}