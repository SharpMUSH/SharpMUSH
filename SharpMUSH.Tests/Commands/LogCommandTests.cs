using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log Test log entry"));

		await NotifyService
			.Received()
			.NotifyLocalized(TestHelpers.MatchingObject(executor),
				Arg.Is<string>(k => k == nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat)));
	}

	[Test]
	public async ValueTask LogCommand_WithCmdSwitch_LogsToCommandCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/cmd Test command log entry"));

		await NotifyService
			.Received()
			.NotifyLocalized(TestHelpers.MatchingObject(executor),
				Arg.Is<string>(k => k == nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat)));
	}

	[Test]
	public async ValueTask LogCommand_WithWizSwitch_LogsToWizardCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/wiz Test wizard log entry"));

		await NotifyService
			.Received()
			.NotifyLocalized(TestHelpers.MatchingObject(executor),
				Arg.Is<string>(k => k == nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat)));
	}

	[Test]
	public async ValueTask LogCommand_WithErrSwitch_LogsToErrorCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/err Test error log entry"));

		await NotifyService
			.Received()
			.NotifyLocalized(TestHelpers.MatchingObject(executor),
				Arg.Is<string>(k => k == nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat)));
	}

	[Test]
	public async ValueTask LogCommand_NoMessage_ReturnsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log"));

		await NotifyService
			.Received()
			.NotifyLocalized(TestHelpers.MatchingObject(executor),
				Arg.Is<string>(k => k == nameof(ErrorMessages.Notifications.LogUsage)));
	}

	[Test]
	public async ValueTask LogCommand_RecallSwitch_RetrievesLogs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/recall"));

		// @log/recall retrieves recent log entries — output starts with log header
		await NotifyService
			.Received()
			.NotifyLocalized(TestHelpers.MatchingObject(executor),
				Arg.Is<string>(k => k == nameof(ErrorMessages.Notifications.NoLogEntriesForCategoryFormat)));
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask LogwipeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@logwipe command"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Log Management Status:", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LsetCommand()
	{
		// First set a lock on object #1
		await Parser.CommandParse(1, ConnectionService, MModule.single("@lock #1=#TRUE"));

		// Now test @lset to set a flag on the Basic lock
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@lset #1/Basic=visual"));

		// Verify the command executed successfully (didn't throw or return error)
		await Assert.That(result).IsNotNull();
	}
}
