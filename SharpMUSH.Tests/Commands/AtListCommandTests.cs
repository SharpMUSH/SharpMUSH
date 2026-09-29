using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
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
		var notifications = WebAppFactoryArg.Notifications;
		var before = notifications.DeliveryCountFor(_player.DbRef);
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list"));

		await Assert.That(notifications.DeliveriesFor(_player.DbRef).Skip(before).Any(delivery =>
			delivery.Sender == _player.DbRef
			&& delivery.Message == ErrorMessages.Notifications.ListNotUnderstood)).IsTrue();
	}

	// PennMUSH src/cmds.c do_list: an unrecognised type gets the same message as no type at all.
	[Test]
	public async ValueTask List_UnknownArgument_DisplaysHelpMessage()
	{
		var notifications = WebAppFactoryArg.Notifications;
		var before = notifications.DeliveryCountFor(_player.DbRef);
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list zorblatt"));

		await Assert.That(notifications.DeliveriesFor(_player.DbRef).Skip(before).Any(delivery =>
			delivery.Sender == _player.DbRef
			&& delivery.Message == ErrorMessages.Notifications.ListNotUnderstood)).IsTrue();
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
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "FUNCTIONS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// do_list matches "flags" with strcasecmp, not string_prefixe: an abbreviation is not a flag list.
	[Test]
	public async ValueTask List_AbbreviatedFlags_IsNotAccepted()
	{
		var notifications = WebAppFactoryArg.Notifications;
		var before = notifications.DeliveryCountFor(_player.DbRef);
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list flag"));

		await Assert.That(notifications.DeliveriesFor(_player.DbRef).Skip(before).Any(delivery =>
			delivery.Sender == _player.DbRef
			&& delivery.Message == ErrorMessages.Notifications.ListNotUnderstood)).IsTrue();
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
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "Flags: abode (a), ansi (a), ")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// PennMUSH src/flags.c do_list_flags: one "Flags: NAME (c), NAME, ..." line from list_all_flags,
	// sorted as ALPHANUM_LIST (strcoll, so punctuation only breaks ties: NOSPOOF sits between NO_LEAVE
	// and NO_TEL). Internal flags are never listed, and mdark ones (NO_LOG, SUSPECT) only to a wizard
	// or royalty; the executor here is a mortal.
	[Test]
	public async ValueTask List_Flags_DisplaysFlagList()
	{
		var text = await ListLineAsync("@list/flags", "Flags: ");

		await Assert.That(text).StartsWith("Flags: ABODE (A), ANSI (A), ");
		await Assert.That(text).Contains("NO_LEAVE (N), NOSPOOF (\"), NO_TEL (N), NO_WARN (w)");
		await Assert.That(text).Contains(", CHAN_USEFIRSTMATCH, CHOWN_OK (C), ");
		var names = text["Flags: ".Length..].Split(", ").Select(entry => entry.Split(' ')[0]).ToArray();
		await Assert.That(names).DoesNotContain("GOING");
		await Assert.That(names).DoesNotContain("GOING_TWICE");
		await Assert.That(names).DoesNotContain("NO_LOG");
	}

	// /lowercase folds the list but not its "Flags" label (do_list_flags lowercases only the list).
	[Test]
	public async ValueTask List_Flags_Lowercase_DisplaysLowercaseFlagList()
	{
		var text = await ListLineAsync("@list/lowercase/flags", "Flags: ");

		await Assert.That(text).StartsWith("Flags: abode (a), ansi (a), ");
	}

	// Powers keep PennMUSH's own spelling of each name: the table-defined ones in mixed case, the ones
	// flags.c adds at startup (Debit, Many_Attribs, hook, Can_dark, Pick_Dbrefs, Can_HTTP) upper-cased.
	[Test]
	public async ValueTask List_Powers_DisplaysPowerList()
	{
		var text = await ListLineAsync("@list/powers", "Powers: ");

		await Assert.That(text).StartsWith(
			"Powers: Announce, Boot, Builder, CAN_DARK, CAN_HTTP, Can_spoof, Chat_Privs, DEBIT, Functions, Guest, Halt, Hide, HOOK, Idle, ");
		await Assert.That(text).Contains(", Long_Fingers, MANY_ATTRIBS, No_Pay, ");
		await Assert.That(text).Contains(", Pemit_All, PICK_DBREFS, Player_Create, ");
	}

	private async ValueTask<string> ListLineAsync(string command, string label)
	{
		var notifications = WebAppFactoryArg.Notifications;
		var before = notifications.DeliveryCountFor(_player.DbRef);
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain(command));

		return notifications.DeliveriesFor(_player.DbRef).Skip(before)
			.Select(delivery => delivery.Message)
			.Single(message => message.StartsWith(label, StringComparison.Ordinal));
	}

	[Test]
	public async ValueTask List_Locks_DisplaysLockTypes()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/locks"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "LOCK TYPES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Attribs_DisplaysStandardAttributes()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/attribs"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "STANDARD ATTRIBUTES:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Commands_DisplaysCommandList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/commands"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "COMMANDS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Functions_DisplaysFunctionList()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/functions"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "FUNCTIONS:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask List_Motd_DisplaysMotdSettings()
	{
		var executor = _player.DbRef;
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain("@list/motd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, "Current Message of the Day settings:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
