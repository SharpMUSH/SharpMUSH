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
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask HelpCommandWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that help command runs and returns the main help page
		await Parser.CommandParse(1, ConnectionService, MModule.single("help"));

		// Verify that NotifyService was called with content containing "help newbie"
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("help newbie")) ||
				(msg.IsT1 && msg.AsT1.Contains("help newbie"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithTopicWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test help with the "newbie" topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("help newbie"));

		// Verify that NotifyService was called with content about newbie help
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("MUSH")) ||
				(msg.IsT1 && msg.AsT1.Contains("MUSH"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithWildcardWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test help with wildcard pattern - should list matching topics
		await Parser.CommandParse(1, ConnectionService, MModule.single("help help*"));

		// Verify that NotifyService was called with a list of matching topics
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && (msg.AsT0.ToString().Contains("help") || msg.AsT0.ToString().Contains("helpfile"))) ||
				(msg.IsT1 && (msg.AsT1.Contains("help") || msg.AsT1.Contains("helpfile")))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpSearchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test help/search switch - should find topics whose body CONTAINS the search term (content search)
		await Parser.CommandParse(1, ConnectionService, MModule.single("help/search newbie"));

		// Verify that NotifyService was called with "Matches:" format (content search result)
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Matches:")) ||
				(msg.IsT1 && msg.AsT1.Contains("Matches:"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test help with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("help nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No entry for" (PennMUSH-compatible message)
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No entry for")) ||
				(msg.IsT1 && msg.AsT1.Contains("No entry for"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithPrefixMatchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that a prefix match finds the topic (PennMUSH behavior: 'help newb' finds 'newbie')
		await Parser.CommandParse(1, ConnectionService, MModule.single("help newb"));

		// Should show the 'newbie' entry content (contains "MUSHing")
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("MUSH")) ||
				(msg.IsT1 && msg.AsT1.Contains("MUSH"))), null, INotifyService.NotificationType.Announce);
	}
}
