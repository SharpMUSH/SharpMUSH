using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AtListCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask List_NoSwitch_DisplaysHelpMessage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list without switches
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list"));

		// Verify that a notification was sent with help message
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "You must specify what to list. Use one of: /MOTD /FUNCTIONS /COMMANDS /ATTRIBS /LOCKS /FLAGS /POWERS /ALLOCATIONS")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Flags_DisplaysFlagList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/flags
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/flags"));

		// Verify that a notification was sent with the flag list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "OBJECT FLAGS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Flags_Lowercase_DisplaysLowercaseFlagList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/lowercase/flags (note: switch order matters)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/lowercase/flags"));

		// Verify that a notification was sent with lowercase flag list
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Object Flags:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Powers_DisplaysPowerList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/powers
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/powers"));

		// Verify that a notification was sent with the power list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "OBJECT POWERS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Locks_DisplaysLockTypes()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/locks
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/locks"));

		// Verify that a notification was sent with lock types
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "LOCK TYPES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Attribs_DisplaysStandardAttributes()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/attribs
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/attribs"));

		// Verify that a notification was sent with standard attributes
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "STANDARD ATTRIBUTES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Commands_DisplaysCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/commands
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/commands"));

		// Verify that a notification was sent with commands
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Functions_DisplaysFunctionList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/functions
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/functions"));

		// Verify that a notification was sent with functions
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "FUNCTIONS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Motd_DisplaysMotdSettings()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Execute @list/motd
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/motd"));

		// Verify that a notification was sent with MOTD settings
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Current Message of the Day settings:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
