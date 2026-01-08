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
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask List_NoSwitch_DisplaysHelpMessage()
	{
		// Execute @list without switches
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list"));

		// Verify that a notification was sent with help message
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("You must specify what to list")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Flags_DisplaysFlagList()
	{
		// Execute @list/flags
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/flags"));

		// Verify that a notification was sent with the flag list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("OBJECT FLAGS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	
	public async ValueTask List_Flags_Lowercase_DisplaysLowercaseFlagList()
	{
		// Execute @list/lowercase/flags (note: switch order matters)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/lowercase/flags"));

		// Verify that a notification was sent with lowercase flag list
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Object Flags:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Powers_DisplaysPowerList()
	{
		// Execute @list/powers
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/powers"));

		// Verify that a notification was sent with the power list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("OBJECT POWERS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Locks_DisplaysLockTypes()
	{
		// Execute @list/locks
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/locks"));

		// Verify that a notification was sent with lock types
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("LOCK TYPES:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Attribs_DisplaysStandardAttributes()
	{
		// Execute @list/attribs
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/attribs"));

		// Verify that a notification was sent with standard attributes
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("STANDARD ATTRIBUTES:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Commands_DisplaysCommandList()
	{
		// Execute @list/commands
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/commands"));

		// Verify that a notification was sent with commands
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("COMMANDS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Functions_DisplaysFunctionList()
	{
		// Execute @list/functions
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/functions"));

		// Verify that a notification was sent with functions
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("FUNCTIONS:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask List_Motd_DisplaysMotdSettings()
	{
		// Execute @list/motd
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/motd"));

		// Verify that a notification was sent with MOTD settings
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Current Message of the Day settings:")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}
}
