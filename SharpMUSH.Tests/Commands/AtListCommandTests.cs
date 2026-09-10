using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AtListCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_player.DbRef, _player.Handle);
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private TestIsolationHelpers.TestPlayer _player = null!;

	[Before(Test)]
	public async Task CreatePlayer()
	{
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AtListCommand");
	}

	[After(Test)]
	public async Task DisconnectPlayer()
	{
		if (_player is not null)
			await ConnectionService.Disconnect(_player.Handle);
	}

	// PennMUSH src/cmds.c do_list: a bare @list falls through to `notify(player,
	// T("I don't understand what you want to @list."))`, the same answer an unrecognised type gets.
	[Test]
	public async ValueTask List_NoSwitch_DisplaysHelpMessage()
	{
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.ListNotUnderstood), _player.DbRef, _player.DbRef)).IsTrue();
	}

	// PennMUSH src/cmds.c do_list: an unrecognised type gets the same message as no type at all.
	[Test]
	public async ValueTask List_UnknownArgument_DisplaysHelpMessage()
	{
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list zorblatt"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.ListNotUnderstood), _player.DbRef, _player.DbRef)).IsTrue();
	}

	// PennMUSH src/cmds.c cmd_list falls through to do_list(executor, arg_left, ...) when no
	// content switch is set, so `@list commands` is the documented spelling (game/txt/hlp/penncmd.hlp:
	// "@list[/lowercase] <switch>") and must produce the same listing as `@list/commands`.
	[Test]
	public async ValueTask List_CommandsArgument_DisplaysCommandList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list commands"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// do_list uses string_prefixe("commands", arg) — any non-empty prefix matches.
	[Test]
	public async ValueTask List_AbbreviatedArgument_DisplaysCommandList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list comm"));

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
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list f"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "FUNCTIONS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// do_list matches "flags" with strcasecmp, not string_prefixe: an abbreviation is not a flag list.
	[Test]
	public async ValueTask List_AbbreviatedFlags_IsNotAccepted()
	{
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list flag"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.ListNotUnderstood), _player.DbRef, _player.DbRef)).IsTrue();
	}

	// The /lowercase modifier is orthogonal to how the type was spelled.
	[Test]
	public async ValueTask List_LowercaseSwitchWithArgument_DisplaysLowercaseList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/lowercase flags"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Flags:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Flags_DisplaysFlagList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/flags"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "OBJECT FLAGS:\nNAME                 SYMBOL TYPE RESTRICTIONS")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Flags_Lowercase_DisplaysLowercaseFlagList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/lowercase/flags"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Flags:\nname                 symbol type restrictions")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Powers_DisplaysPowerList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/powers"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "OBJECT POWERS:\nNAME                 SYMBOL ALIAS              TYPE RESTRICTIONS")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Locks_DisplaysLockTypes()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/locks"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "LOCK TYPES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Attribs_DisplaysStandardAttributes()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/attribs"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "STANDARD ATTRIBUTES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Commands_DisplaysCommandList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/commands"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Functions_DisplaysFunctionList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/functions"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "FUNCTIONS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Motd_DisplaysMotdSettings()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/motd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Current Message of the Day settings:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
