using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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
		// Test that news command runs and returns the main news page
		await Parser.CommandParse(1, ConnectionService, MModule.single("news"));

		// Verify that NotifyService was called with content about news
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("news")) ||
				(msg.IsT1 && msg.AsT1.Contains("news"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NewsWithTopicWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NewsTopic");
		// Test news with the "welcome" topic
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("news welcome"));

		// Verify that NotifyService was called with content about welcome
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("SharpMUSH")) ||
				(msg.IsT1 && msg.AsT1.Contains("SharpMUSH"))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NewsWithWildcardWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Use a pattern that matches no topics → NewsNoNewsForTopic is deterministically sent.
		await Parser.CommandParse(1, ConnectionService, MModule.single("news *xyznonexistent99*"));

		// Wildcard with 0 matches sends NewsNoNewsForTopic.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NewsNoNewsForTopic), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask NewsNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test news with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("news nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No news available"
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
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask AhelpCommandAndAnewsAliasWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		
		// Test that ahelp command runs for God (player 1)
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("anews"));

		// Verify that NotifyService was called with content about ahelp
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Admin help system for SharpMUSH.")) ||
				(msg.IsT1 && msg.AsT1.Contains("Admin help system for SharpMUSH."))), 
					TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AhelpWithTopicWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test ahelp with the "security" topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp security"));

		// Verify that NotifyService was called with content about security
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("SharpMUSH includes comprehensive security features to protect your MUSH:")) ||
				(msg.IsT1 && msg.AsT1.Contains("SharpMUSH includes comprehensive security features to protect your MUSH:"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AhelpNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test ahelp with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No admin help available for '<topic>'"
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AhelpNoHelpForTopic), executor, executor)).IsTrue();
	}
}
