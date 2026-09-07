using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
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
	public async ValueTask TeachCommandEchoesToExecutorAndExecutesOnce()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var marker = $"TeachSelf_{Guid.NewGuid():N}";
		var taughtCommand = $"think {marker}";

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"teach {taughtCommand}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage($"God types --> {taughtCommand}"),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage(marker),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask TeachListEchoesToExecutorAndExecutesOnce()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var marker = $"TeachListSelf_{Guid.NewGuid():N}";
		var taughtActionList = $"think {marker}";

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"teach/list {taughtActionList}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage($"God types --> {taughtActionList}"),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage(marker),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

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
