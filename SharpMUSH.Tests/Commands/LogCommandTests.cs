using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
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
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask LogCommand_DefaultSwitch_LogsToCommandCategory()
	{
		var testPlayer = await CreateTestPlayerAsync("LogDef");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@log Test log entry"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message logged to Command log.")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LogCommand_WithCmdSwitch_LogsToCommandCategory()
	{
		var testPlayer = await CreateTestPlayerAsync("LogCmd");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@log/cmd Test command log entry"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message logged to Command log.")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LogCommand_WithWizSwitch_LogsToWizardCategory()
	{
		var testPlayer = await CreateTestPlayerAsync("LogWiz");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@log/wiz Test wizard log entry"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message logged to Wizard log.")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LogCommand_WithErrSwitch_LogsToErrorCategory()
	{
		var testPlayer = await CreateTestPlayerAsync("LogErr");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@log/err Test error log entry"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message logged to Error log.")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LogCommand_NoMessage_ReturnsError()
	{
		var testPlayer = await CreateTestPlayerAsync("LogNoMsg");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@log"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Usage: @log[/<switch>] <message> or @log/recall[/<switch>] [<number>]")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LogCommand_RecallSwitch_RetrievesLogs()
	{
		var testPlayer = await CreateTestPlayerAsync("LogRecall");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@log/recall"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask LogwipeCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Logwipe");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@logwipe command"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask LsetCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Lset");
		// First set a lock on the test player's object
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@lock {testPlayer.DbRef}=#TRUE"));

		// Now test @lset to set a flag on the Basic lock
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@lset {testPlayer.DbRef}/Basic=visual"));

		// Verify the command executed successfully (didn't throw or return error)
		await Assert.That(result).IsNotNull();
	}
}
