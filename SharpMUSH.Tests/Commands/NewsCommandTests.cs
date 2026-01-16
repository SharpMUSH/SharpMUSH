using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NewsCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.CommandParser;
	private INotifyService NotifyService => Factory.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask NewsCommandWorks()
	{
		// Test that news command runs and returns the main news page
		await Parser.CommandParse(1, ConnectionService, MModule.single("news"));

		// Verify that NotifyService was called with content about news
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("news")) ||
				(msg.IsT1 && msg.AsT1.Contains("news"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask NewsWithTopicWorks()
	{
		// Test news with the "welcome" topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("news welcome"));

		// Verify that NotifyService was called with content about welcome
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("SharpMUSH")) ||
				(msg.IsT1 && msg.AsT1.Contains("SharpMUSH"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask NewsWithWildcardWorks()
	{
		// Test news with wildcard pattern - should list matching topics
		await Parser.CommandParse(1, ConnectionService, MModule.single("news *news*"));

		// Verify that NotifyService was called with matching topics
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask NewsNonExistentTopic()
	{
		// Test news with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("news nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No news available"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No news available")) ||
				(msg.IsT1 && msg.AsT1.Contains("No news available"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}
}

public class AhelpCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.CommandParser;
	private INotifyService NotifyService => Factory.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask AhelpCommandWorks()
	{
		// Test that ahelp command runs for God (player 1)
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp"));

		// Verify that NotifyService was called with content about ahelp
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && (msg.AsT0.ToString().Contains("ahelp") || msg.AsT0.ToString().Contains("admin"))) ||
				(msg.IsT1 && (msg.AsT1.Contains("ahelp") || msg.AsT1.Contains("admin")))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask AhelpWithTopicWorks()
	{
		// Test ahelp with the "security" topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp security"));

		// Verify that NotifyService was called with content about security
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Security")) ||
				(msg.IsT1 && msg.AsT1.Contains("Security"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask AnewsAliasWorks()
	{
		// Test that anews is an alias for ahelp
		await Parser.CommandParse(1, ConnectionService, MModule.single("anews"));

		// Verify that NotifyService was called with ahelp content
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && (msg.AsT0.ToString().Contains("ahelp") || msg.AsT0.ToString().Contains("admin"))) ||
				(msg.IsT1 && (msg.AsT1.Contains("ahelp") || msg.AsT1.Contains("admin")))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask AhelpNonExistentTopic()
	{
		// Test ahelp with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("ahelp nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No admin help available"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No admin help available")) ||
				(msg.IsT1 && msg.AsT1.Contains("No admin help available"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}
}
