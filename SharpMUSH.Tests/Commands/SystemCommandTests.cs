using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class SystemCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	// PennMUSH reference: cmd_flag with SWITCH_LIST calls do_list_flags("FLAG", ..., FLAG_LIST_NAMECHAR, T("Flags"))
	// SharpMUSH outputs a table beginning with "Object Flags:".
	[Test]
	public async ValueTask FlagCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/list"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextContains(msg, "Object Flags:")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: cmd_power with SWITCH_LIST calls do_list_flags("POWER", ..., FLAG_LIST_NAMECHAR, T("Powers"))
	// SharpMUSH outputs a table beginning with "Object Powers:".
	[Test]
	public async ValueTask PowerCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/list"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextContains(msg, "Object Powers:")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: cmd_hook with SWITCH_LIST calls do_hook_list(executor, arg_left, 1).
	// With a command name argument but no hooks set, SharpMUSH outputs "No hooks set for command '<CMD>'."
	[Test]
	public async ValueTask HookCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hook/list @emit"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextContains(msg, "No hooks set for command '@EMIT'.")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: cmd_function with no args calls do_function(executor, NULL, NULL, 0)
	// which lists user-defined and built-in functions. Output begins with "Global user-defined functions:".
	[Test]
	public async ValueTask FunctionCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextContains(msg, "Global user-defined functions:")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: @command with no arg returns an error.
	// SharpMUSH @command has no /list switch; calling @command/list returns an invalid-switch error.
	// The expected full output is "#-1 INVALID SWITCH: list".
	[Test]
	public async ValueTask CommandCommand()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@command/list"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo("#-1 INVALID SWITCH: list");
	}

	// PennMUSH reference: cmd_hide calls hide_player(executor, status, arg_left).
	// @HIDE acts on the executor. /off ensures a known starting state, /on then hides the executor.
	// Expected: "You are now hidden from the WHO list."
	[Test]
	public async ValueTask HideCommand()
	{
		// Ensure executor starts visible so the subsequent /on produces a deterministic message.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessageEquals(msg, "You are now hidden from the WHO list.")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());

		// Restore to visible state.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
	}

	// PennMUSH reference: cmd_kick calls do_kick(executor, arg_left).
	// Kicking a player who has no active connections outputs "That player is not connected."
	[Test]
	public async ValueTask KickCommand()
	{
		var testPlayerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "SystemTestKick");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@kick {testPlayerDbRef}"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessageEquals(msg, "That player is not connected.")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: do_attribute_access outputs:
	//   notify_format(player, T("%s -- Attribute permissions now: %s"), name, privs_to_string(attr_privs_view, flags))
	// e.g. "MYATTR -- Attribute permissions now: wizard"
	[Test]
	public async ValueTask AttributeCommand()
	{
		var uniqueAttr = TestIsolationHelpers.GenerateUniqueName("SYSCMD_ATTRTEST").ToUpperInvariant();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@attribute/access {uniqueAttr}=wizard"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageEquals(msg, $"{uniqueAttr} -- Attribute permissions now: wizard")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: do_atrlock on success outputs "Attribute locked." (attrib.c).
	// The attribute must already exist before @atrlock can lock it.
	[Test]
	public async ValueTask AtrlockCommand()
	{
		var testDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SystemTestAtrlock");
		var uniqueAttr = TestIsolationHelpers.GenerateUniqueName("SYSCMD_ATRLOCKATTR").ToUpperInvariant();

		// Create the attribute first so it exists on the object.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&{uniqueAttr} {testDbRef}=atrlock_test_value"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@atrlock {testDbRef}/{uniqueAttr}=on"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessageEquals(msg, "Attribute locked.")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: do_atrchown on success outputs "Attribute owner changed." (attrib.c).
	// The attribute must already exist before @atrchown can change its owner.
	[Test]
	public async ValueTask AtrchownCommand()
	{
		var sourceDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SystemTestAtrchownSrc");
		var targetPlayerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "SystemTestAtrchownPly");
		var uniqueAttr = TestIsolationHelpers.GenerateUniqueName("SYSCMD_ATRCHOWNATTR").ToUpperInvariant();

		// Create the attribute first so it exists on the object.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&{uniqueAttr} {sourceDbRef}=atrchown_test_value"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@atrchown {sourceDbRef}/{uniqueAttr}={targetPlayerDbRef}"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessageEquals(msg, "Attribute owner changed.")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	// PennMUSH reference: do_firstexit re-links an exit to move it to the front of the room's exit list.
	// No output is produced on success in PennMUSH. @firstexit takes exit names as space-separated args (CMD_T_ARGS).
	[Test]
	public async ValueTask FirstexitCommand()
	{
		var exitName = TestIsolationHelpers.GenerateUniqueName("SystemTestFirstexitExit");
		var exitResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@open {exitName}"));
		var exitMessage = exitResult.Message?.ToPlainText()
			?? throw new InvalidOperationException($"@open {exitName} returned a null message.");
		var exitDbRef = DBRef.Parse(exitMessage);

		// @firstexit is silent on success (PennMUSH: do_firstexit produces no notify).
		// Pass just the exit dbref as a space-separated argument (not room=exit).
		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@firstexit {exitDbRef}"));

		await Assert.That(result.Message?.ToPlainText() ?? string.Empty).IsEqualTo(string.Empty);
	}
}
