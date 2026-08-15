using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

// ClearReceivedCalls below wipes the session-shared Notify substitute, so this class may not run
// alongside another that reads it.
[NotInParallel]
public class AtListCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	// Several tests in this class produce byte-identical output ("COMMANDS:", "FUNCTIONS:",
	// "Object Flags:") through the switch spelling and the argument spelling, so a session-shared
	// Notify substitute would make every Received(1) count the other tests' calls too.
	[Before(Test)]
	public void ResetNotifications() => NotifyService.ClearReceivedCalls();

	// PennMUSH src/cmds.c do_list: a bare @list falls through to `notify(player,
	// T("I don't understand what you want to @list."))`, the same answer an unrecognised type gets.
	[Test]
	public async ValueTask List_NoSwitch_DisplaysHelpMessage()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.ListNotUnderstood))).IsTrue();
	}

	// PennMUSH src/cmds.c do_list: an unrecognised type gets the same message as no type at all.
	[Test]
	public async ValueTask List_UnknownArgument_DisplaysHelpMessage()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list zorblatt"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.ListNotUnderstood))).IsTrue();
	}

	// PennMUSH src/cmds.c cmd_list falls through to do_list(executor, arg_left, ...) when no
	// content switch is set, so `@list commands` is the documented spelling (game/txt/hlp/penncmd.hlp:
	// "@list[/lowercase] <switch>") and must produce the same listing as `@list/commands`.
	[Test]
	public async ValueTask List_CommandsArgument_DisplaysCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list commands"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// do_list uses string_prefixe("commands", arg) — any non-empty prefix matches.
	[Test]
	public async ValueTask List_AbbreviatedArgument_DisplaysCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list comm"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// do_list tests "commands" then "functions" by prefix before it reaches the exact-match-only
	// "flags", so a bare "f" is functions in PennMUSH, not flags.
	[Test]
	public async ValueTask List_SingleLetterF_ResolvesToFunctionsNotFlags()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list f"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "FUNCTIONS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// do_list matches "flags" with strcasecmp, not string_prefixe: an abbreviation is not a flag list.
	[Test]
	public async ValueTask List_AbbreviatedFlags_IsNotAccepted()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list flag"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.ListNotUnderstood))).IsTrue();
	}

	// The /lowercase modifier is orthogonal to how the type was spelled.
	[Test]
	public async ValueTask List_LowercaseSwitchWithArgument_DisplaysLowercaseList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/lowercase flags"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Flags:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Flags_DisplaysFlagList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/flags"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "OBJECT FLAGS:\nNAME                 SYMBOL TYPE RESTRICTIONS")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Flags_Lowercase_DisplaysLowercaseFlagList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/lowercase/flags"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Flags:\nname                 symbol type restrictions")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Powers_DisplaysPowerList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/powers"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "OBJECT POWERS:\nNAME                 SYMBOL ALIAS              TYPE RESTRICTIONS")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Locks_DisplaysLockTypes()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/locks"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "LOCK TYPES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Attribs_DisplaysStandardAttributes()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/attribs"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "STANDARD ATTRIBUTES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Commands_DisplaysCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/commands"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Functions_DisplaysFunctionList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/functions"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "FUNCTIONS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Motd_DisplaysMotdSettings()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list/motd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Current Message of the Day settings:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
