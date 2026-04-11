using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
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
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("news")) ||
				(msg.IsT1 && msg.AsT1.Contains("news"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NewsWithTopicWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test news with the "welcome" topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("news welcome"));

		// Verify that NotifyService was called with content about welcome
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("SharpMUSH")) ||
				(msg.IsT1 && msg.AsT1.Contains("SharpMUSH"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NewsWithWildcardWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test news with wildcard pattern - should list matching topics
		await Parser.CommandParse(1, ConnectionService, MModule.single("news *news*"));

		// Verify that NotifyService was called with matching topics or "No news available"
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "No news available for '*news*'.") ||
				TestHelpers.MessagePlainTextStartsWith(msg, "News topics matching '*news*':")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NewsNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test news with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("news nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No news available"
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No news available")) ||
				(msg.IsT1 && msg.AsT1.Contains("No news available"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}

[NotInParallel]
public class AhelpCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask AhelpCommandWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that ahelp command runs for God (player 1)
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp"));

		// Verify that NotifyService was called with content about ahelp
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && (msg.AsT0.ToString().Contains("ahelp") || msg.AsT0.ToString().Contains("admin"))) ||
				(msg.IsT1 && (msg.AsT1.Contains("ahelp") || msg.AsT1.Contains("admin")))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AhelpWithTopicWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test ahelp with the "security" topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp security"));

		// Verify that NotifyService was called with content about security
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Security")) ||
				(msg.IsT1 && msg.AsT1.Contains("Security"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AnewsAliasWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that anews is an alias for ahelp
		await Parser.CommandParse(1, ConnectionService, MModule.single("anews"));

		// Verify that NotifyService was called with ahelp content
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && (msg.AsT0.ToString().Contains("ahelp") || msg.AsT0.ToString().Contains("admin"))) ||
				(msg.IsT1 && (msg.AsT1.Contains("ahelp") || msg.AsT1.Contains("admin")))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AhelpNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test ahelp with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No admin help available"
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No admin help available")) ||
				(msg.IsT1 && msg.AsT1.Contains("No admin help available"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
