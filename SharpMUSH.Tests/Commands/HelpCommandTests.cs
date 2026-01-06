using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Documentation;
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
		// Test that help command runs without error
		await Parser.CommandParse(1, ConnectionService, MModule.single("help"));

		// Verify that NotifyService was called (help output was sent)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpWithTopicWorks()
	{
		// Test help with a specific topic
		await Parser.CommandParse(1, ConnectionService, MModule.single("help newbie"));

		// Verify that NotifyService was called
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpWithWildcardWorks()
	{
		// Test help with wildcard pattern
		await Parser.CommandParse(1, ConnectionService, MModule.single("help @*"));

		// Verify that NotifyService was called
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpSearchWorks()
	{
		// Test help/search switch
		await Parser.CommandParse(1, ConnectionService, MModule.single("help/search newbie"));

		// Verify that NotifyService was called
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask HelpNonExistentTopic()
	{
		// Test help with a topic that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("help nonexistenttopicxyz123"));

		// Verify that NotifyService was called with either "No help available" or was initialized
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}
}
