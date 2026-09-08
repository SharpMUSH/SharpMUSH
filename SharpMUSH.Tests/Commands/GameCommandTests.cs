using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class GameCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BuyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("buy sword"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You try to buy 'sword'.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask ScoreCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("score"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "The SCORE command is not supported.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask TeachCommandEchoesToRoomAndExecutesOnce()
	{
		var executor = await CreatePlayerAsync("TeachExecutor");
		var observer = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "TeachObserver");
		try
		{
			var marker = $"TeachSelf_{Guid.NewGuid():N}";
			var taughtCommand = $"think {marker}";
			var echo = $"{executor.Name} types --> {taughtCommand}";
			var executorStart = Notifications.CountFor(executor.DbRef);
			var observerStart = Notifications.CountFor(observer);

			await CommandAsAsync(executor, $"teach {taughtCommand}");

			var executorMessages = Notifications.For(executor.DbRef).Skip(executorStart);
			var observerMessages = Notifications.For(observer).Skip(observerStart);
			await Assert.That(executorMessages.Count(message => message == echo)).IsEqualTo(1);
			await Assert.That(observerMessages.Count(message => message == echo)).IsEqualTo(1);
			await Assert.That(executorMessages.Count(message => message == marker)).IsEqualTo(1);
		}
		finally
		{
			await ConnectionService.Disconnect(executor.Handle);
		}
	}

	[Test]
	[Arguments("teach", "Teach what?")]
	[Arguments("teach/list", "Teach what action list?")]
	public async ValueTask TeachWithoutArgumentReportsExpectedError(string command, string expectedMessage)
	{
		var executor = await CreatePlayerAsync("TeachMissingArgumentExecutor");
		try
		{
			var start = Notifications.CountFor(executor.DbRef);

			await CommandAsAsync(executor, command);

			var messages = Notifications.For(executor.DbRef).Skip(start).ToArray();
			await Assert.That(messages).IsEquivalentTo([expectedMessage]);
		}
		finally
		{
			await ConnectionService.Disconnect(executor.Handle);
		}
	}

	[Test]
	public async ValueTask TeachCommandEchoesReportedSetFormToExecutor()
	{
		var player = await CreatePlayerAsync("TeachSetPlayer");
		try
		{
			const string taughtCommand = "@set me=color";
			var echo = $"{player.Name} types --> {taughtCommand}";
			var start = Notifications.CountFor(player.DbRef);

			await CommandAsAsync(player, $"teach {taughtCommand}");

			var messages = Notifications.For(player.DbRef).Skip(start);
			await Assert.That(messages.Count(message => message == echo)).IsEqualTo(1);
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async ValueTask TeachListEchoesToExecutorAndExecutesEveryCommandOnce()
	{
		var executor = await CreatePlayerAsync("TeachListExecutor");
		try
		{
			var firstMarker = $"TeachListFirst_{Guid.NewGuid():N}";
			var secondMarker = $"TeachListSecond_{Guid.NewGuid():N}";
			var taughtActionList = $"think {firstMarker};think {secondMarker}";
			var echo = $"{executor.Name} types --> {taughtActionList}";
			var start = Notifications.CountFor(executor.DbRef);

			await CommandAsAsync(executor, $"teach/list {taughtActionList}");

			var messages = Notifications.For(executor.DbRef).Skip(start);
			await Assert.That(messages.Count(message => message == echo)).IsEqualTo(1);
			await Assert.That(messages.Count(message => message == firstMarker)).IsEqualTo(1);
			await Assert.That(messages.Count(message => message == secondMarker)).IsEqualTo(1);
		}
		finally
		{
			await ConnectionService.Disconnect(executor.Handle);
		}
	}

	private async Task<TeachPlayer> CreatePlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;

		return new TeachPlayer(player.DbRef, player.Handle, playerObject.Object().Name);
	}

	private async Task CommandAsAsync(TeachPlayer player, string command)
	{
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
	}

	private sealed record TeachPlayer(DBRef DbRef, long Handle, string Name);

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask FollowCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("follow #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You can't follow yourself.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnfollowCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("unfollow"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You aren't following anyone.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DesertCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("desert"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You stop following and dismiss all followers.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DismissCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("dismiss #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You dismiss all your followers. (0 dismissed)", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask EmptyCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create EmptyTestContainer"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create EmptyTestItem1"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create EmptyTestItem2"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set EmptyTestContainer=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get EmptyTestContainer"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give EmptyTestContainer=EmptyTestItem1"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give EmptyTestContainer=EmptyTestItem2"));

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("empty EmptyTestContainer"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask EmptyCommandSameLocation()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create EmptyTestBox"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create EmptyTestThing"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set EmptyTestBox=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give EmptyTestBox=EmptyTestThing"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("drop EmptyTestBox"));

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("empty EmptyTestBox"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WithCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("with #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Do what with them?", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
