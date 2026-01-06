using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class HelpCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask HelpCommandWorks()
	{
		// Test that help command runs and returns the main help page
		await Parser.CommandParse(1, ConnectionService, MModule.single("help"));

		// Verify that NotifyService was called with content containing "help newbie"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("help newbie")) ||
				(msg.IsT1 && msg.AsT1.Contains("help newbie"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpWithTopicWorks()
	{
		// Test help with the "newbie" topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("help newbie"));

		// Verify that NotifyService was called with content about newbie help
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("MUSH")) ||
				(msg.IsT1 && msg.AsT1.Contains("MUSH"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpWithWildcardWorks()
	{
		// Test help with wildcard pattern - should list matching topics
		await Parser.CommandParse(1, ConnectionService, MModule.single("help help*"));

		// Verify that NotifyService was called with a list of matching topics
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && (msg.AsT0.ToString().Contains("help") || msg.AsT0.ToString().Contains("helpfile"))) ||
				(msg.IsT1 && (msg.AsT1.Contains("help") || msg.AsT1.Contains("helpfile")))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpSearchWorks()
	{
		// Test help/search switch - should find topics containing the search term
		await Parser.CommandParse(1, ConnectionService, MModule.single("help/search newbie"));

		// Verify that NotifyService was called with search results
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("newbie")) ||
				(msg.IsT1 && msg.AsT1.Contains("newbie"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpNonExistentTopic()
	{
		// Test help with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("help nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No help available"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No help available")) ||
				(msg.IsT1 && msg.AsT1.Contains("No help available"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}
}
