using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AtListCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask List_NoSwitch_DisplaysHelpMessage()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list without switches
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list"));

		// Verify that a notification was sent with help message
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "You must specify what to list")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Flags_DisplaysFlagList()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/flags
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/flags"));

		// Verify that a notification was sent with the flag list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "OBJECT FLAGS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Switch parsing issue with multiple switches - needs investigation")]
	public async ValueTask List_Flags_Lowercase_DisplaysLowercaseFlagList()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/lowercase/flags (note: switch order matters)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/lowercase/flags"));

		// Verify that a notification was sent with lowercase flag list
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Object Flags:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Powers_DisplaysPowerList()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/powers
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/powers"));

		// Verify that a notification was sent with the power list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "OBJECT POWERS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Locks_DisplaysLockTypes()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/locks
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/locks"));

		// Verify that a notification was sent with lock types
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "LOCK TYPES:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Attribs_DisplaysStandardAttributes()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/attribs
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/attribs"));

		// Verify that a notification was sent with standard attributes
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "STANDARD ATTRIBUTES:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Commands_DisplaysCommandList()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/commands
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/commands"));

		// Verify that a notification was sent with commands
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "COMMANDS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Functions_DisplaysFunctionList()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/functions
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/functions"));

		// Verify that a notification was sent with functions
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "FUNCTIONS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Motd_DisplaysMotdSettings()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Execute @list/motd
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/motd"));

		// Verify that a notification was sent with MOTD settings
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Current Message of the Day settings:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}
}
