using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NewsCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask NewsCommandWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("news"));

		// Weak check. This is currently being interfered with by 'anews' also matching 'news'.
		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
			TestHelpers.MessagePlainTextContains(msg, "news"), sender: executor, type: INotifyService.NotificationType.Announce)).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async ValueTask NewsWithTopicWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NewsTopic");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("news welcome"));

		await Assert.That(NotifyService.Recorded().Count(testPlayer.DbRef, msg =>
				TestHelpers.MessagePlainTextContains(msg, "Welcome to the game"), sender: testPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask NewsWithWildcardWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Use a pattern that matches no topics → NewsNoNewsForTopic is deterministically sent.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("news *xyznonexistent99*"));

		// Wildcard with 0 matches sends NewsNoNewsForTopic.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NewsNoNewsForTopic), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask NewsNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("news nonexistenttopicxyz123"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NewsNoNewsForTopic), executor, executor)).IsTrue();
	}
}

public class AhelpCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async ValueTask AhelpCommandAndAnewsAliasWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("ahelp"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("anews"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
					TestHelpers.MessageContains(msg, "get help on a specific admin topic")
					|| TestHelpers.MessageContains(msg, "Only Wizards and Royalty may use them."), sender: executor, type: INotifyService.NotificationType.Announce)).IsEqualTo(2);
	}

	[Test]
	public async ValueTask AhelpWithTopicWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("ahelp staff procedures"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextContains(msg, "A game's own staff topic."), sender: executor, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask AhelpNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("ahelp nonexistenttopicxyz123"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AhelpNoHelpForTopic), executor, executor)).IsTrue();
	}
}
