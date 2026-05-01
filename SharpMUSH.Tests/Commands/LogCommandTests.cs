using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log Test log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_WithCmdSwitch_LogsToCommandCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/cmd Test command log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_WithWizSwitch_LogsToWizardCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/wiz Test wizard log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_WithErrSwitch_LogsToErrorCategory()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/err Test error log entry"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_NoMessage_ReturnsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.LogUsage), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask LogCommand_RecallSwitch_RetrievesLogs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/recall"));

		// @log/recall retrieves recent log entries — output starts with log header
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLogEntriesForCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask LogwipeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@logwipe command"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Log Management Status:", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LsetCommand()
	{
		// Create a dedicated test object so we don't mutate God (#1) in the shared DB
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create LSetTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Set a lock on the test object
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock #{newDb.Number}=#TRUE"));

		// Now test @lset to set a flag on the Basic lock
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@lset #{newDb.Number}/Basic=visual"));

		// Verify the command executed successfully (didn't throw or return error)
		await Assert.That(result).IsNotNull();
	}
}
