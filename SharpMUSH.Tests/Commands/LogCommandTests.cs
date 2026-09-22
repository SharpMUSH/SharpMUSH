using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class LogCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask LogCommand_DefaultSwitch_LogsToCommandCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@log Test log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_WithCmdSwitch_LogsToCommandCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@log/cmd Test command log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_WithWizSwitch_LogsToWizardCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@log/wiz Test wizard log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_WithErrSwitch_LogsToErrorCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@log/err Test error log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_NoMessage_ReturnsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@log"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.LogUsage), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_RecallSwitch_RetrievesLogs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@log/recall"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLogEntriesForCategoryFormat), executor, executor)).IsTrue();
	}

	private async Task<(List<string> Messages, CallState Result)> LogwipeAs(long handle, DBRef who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		var result = await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return ([.. WebAppFactoryArg.Notifications.For(who).Skip(before)], result);
	}

	/// <summary>
	/// <c>cmd_logwipe</c> (<c>src/cmds.c:971</c>) picks a log from its switches (default
	/// <c>/err</c>) and a policy (default <c>/wipe</c>), then acts on that log file. SharpMUSH owns no
	/// log file — its logs go to the configured Serilog sinks — so every policy is refused, by name,
	/// rather than previewed with "Would …" as though it could be done.
	/// </summary>
	[Test]
	[Arguments("@logwipe/wiz/rotate secret", "rotate", "wiz")]
	[Arguments("@logwipe/conn/trim secret", "trim", "conn")]
	[Arguments("@logwipe/cmd secret", "wipe", "cmd")]
	[Arguments("@logwipe secret", "wipe", "err")]
	public async ValueTask Logwipe_RefusesEveryPolicyItCannotPerform(string command, string policy, string log)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var (messages, result) = await LogwipeAs(1, executor, command);

		await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.LogWipeUnsupportedFormat, policy, log));
		await Assert.That(messages.Any(m => m.StartsWith("@logwipe", StringComparison.OrdinalIgnoreCase) && m.Contains("Would", StringComparison.Ordinal))).IsFalse();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.ErrorNotSupported);
	}

	/// <summary><c>/check</c> names Penn's LT_CHECK log; it is not a dry-run action.</summary>
	[Test]
	public async ValueTask Logwipe_CheckSwitch_NamesTheCheckLog()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var (messages, _) = await LogwipeAs(1, executor, "@logwipe/check secret");

		await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.LogWipeUnsupportedFormat, "wipe", "check"));
	}

	/// <summary><c>@logwipe</c> is <c>CMD_T_GOD</c> (<c>src/command.c:203</c>).</summary>
	[Test]
	public async ValueTask Logwipe_MortalIsRefusedBeforeAnything()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), ConnectionService, "LogwipeMortal");
		var (messages, _) = await LogwipeAs(mortal.Handle, mortal.DbRef, "@logwipe/wiz secret");

		await Assert.That(messages.Any(m => m.StartsWith("@logwipe", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async ValueTask LsetCommand()
	{
		// Create a dedicated test object so we don't mutate God (#1) in the shared DB
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create LSetTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock #{newDb.Number}=#TRUE"));

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lset #{newDb.Number}/Basic=visual"));

		await Assert.That(result).IsNotNull();
	}
}
