using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

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
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpWorks");
		// Test that help command runs and returns the main help page
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help"));

		// Verify that NotifyService was called with content containing "help newbie"
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("help newbie")) ||
				(msg.IsT1 && msg.AsT1.Contains("help newbie"))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithTopicWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpTopic");
		// Test help with the "newbie" topic
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help newbie"));

		// The 'newbie' entry renders to exactly this body; assert the full opening
		// paragraph verbatim rather than just the substring "MUSH", so the test fails
		// if the wrong entry, a truncated body, or unrelated content is delivered.
		const string expected =
			"If you are new to MUSHing, the help files may seem confusing. Most of them are written in a specific style, however, and once you understand it the files are extremely helpful.";

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains(expected)) ||
				(msg.IsT1 && msg.AsT1.Contains(expected))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithWildcardWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpWildcard");
		// Test help with wildcard pattern - should list matching topics
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help help*"));

		// Verify that NotifyService was called with a list of matching topics
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Here are the entries which match 'help*'")) ||
				(msg.IsT1 && msg.AsT1.Contains("Here are the entries which match 'help*'"))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
		
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("help, help query, help search, help/search, helpfile, helpfile2")) ||
				(msg.IsT1 && msg.AsT1.Contains("help, help query, help search, help/search, helpfile, helpfile2"))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpSearchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test help/search switch - should find topics whose body CONTAINS the search term (content search)
		await Parser.CommandParse(1, ConnectionService, MModule.single("help/search newbie"));

		// Verify that NotifyService was called with "Matches:" format (content search result)
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Matches:")) ||
				(msg.IsT1 && msg.AsT1.Contains("Matches:"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test help with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("help nonexistenttopicxyz123"));

		// Verify that NotifyService was called with "No entry for" (PennMUSH-compatible message)
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No entry for")) ||
				(msg.IsT1 && msg.AsT1.Contains("No entry for"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithPrefixMatchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that a prefix match finds the topic (PennMUSH behavior: 'help newb' finds 'newbie')
		await Parser.CommandParse(1, ConnectionService, MModule.single("help newb"));

		// Should show the 'newbie' entry content (contains "MUSHing")
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("If you are new to MUSHing")) ||
				(msg.IsT1 && msg.AsT1.Contains("If you are new to MUSHing"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
