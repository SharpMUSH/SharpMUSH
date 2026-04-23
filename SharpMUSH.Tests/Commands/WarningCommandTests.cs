using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class WarningCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWarningService WarningService => WebAppFactoryArg.Services.GetRequiredService<IWarningService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>
	/// Creates a fresh player with a registered connection handle so that
	/// <c>Parser.CommandParse(testPlayer.Handle, …)</c> executes as that player.
	/// Pattern C: using a unique receiver makes Received(1) unambiguous even when
	/// the message text is a fixed server-generated string.
	/// </summary>
	private Task<TestIsolationHelpers.TestPlayer> CreateFreshPlayerAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	[Test]
	public async Task WarningsCommand_SetToNormal()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// ParseWarnings("normal") → WarningType.Normal; UnparseWarnings → "normal".
		var freshPlayer = await CreateFreshPlayerAsync("WT_Normal");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@warnings me=normal"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Warnings set to: normal")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task WarningsCommand_SetToAll()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// ParseWarnings("all") → WarningType.All; UnparseWarnings → "all".
		var freshPlayer = await CreateFreshPlayerAsync("WT_All");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@warnings me=all"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Warnings set to: all")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task WarningsCommand_SetToNone()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// ParseWarnings("none") → WarningType.None → "Warnings cleared." branch.
		var freshPlayer = await CreateFreshPlayerAsync("WT_None");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@warnings me=none"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Warnings cleared.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task WarningsCommand_WithNegation()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// ParseWarnings("all !exit-desc") → All & ~ExitDesc = Extra; UnparseWarnings → "extra".
		var freshPlayer = await CreateFreshPlayerAsync("WT_Negate");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@warnings me=all !exit-desc"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Warnings set to: extra")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task WarningsCommand_WithUnknownWarning()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// @warnings sends "Unknown warning: unknown-warning" for each unrecognised token.
		var freshPlayer = await CreateFreshPlayerAsync("WT_Unknown");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@warnings me=unknown-warning"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Unknown warning: unknown-warning")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task WarningsCommand_NoArguments_ShowsUsage()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// @warnings with no object arg sends "Usage: @warnings <object>=<warning list>" as the first line.
		var freshPlayer = await CreateFreshPlayerAsync("WT_Usage");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@warnings"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Usage: @warnings <object>=<warning list>")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task WCheckCommand_SpecificObject()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// Fresh player checks their own DBRef (they own themselves → permission passes).
		var freshPlayer = await CreateFreshPlayerAsync("WT_WCheck");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService,
			MModule.single($"@wcheck #{freshPlayer.DbRef.Number}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "@wcheck complete.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task WCheckCommand_NoArguments_ShowsUsage()
	{
		// Pattern C: fresh player is the unique receiver/sender for this test's notification.
		// @wcheck with no argument sends the usage string.
		var freshPlayer = await CreateFreshPlayerAsync("WT_WCheckUsage");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@wcheck"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Usage: @wcheck <object> or @wcheck/me or @wcheck/all")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Integration test - requires proper object setup")]
	public async Task WCheckCommand_WithMe_ChecksOwnedObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - check owned objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck/me"));

		// Assert - should complete check
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Checking objects you own...")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Integration test - requires wizard permissions")]
	public async Task WCheckCommand_WithAll_RequiresWizard()
	{
		// This test would need to set up a wizard player
		await ValueTask.CompletedTask;
	}
}
