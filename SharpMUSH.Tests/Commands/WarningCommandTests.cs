using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
		// ParseWarnings("normal") → WarningType.Normal; UnparseWarnings → "normal".
		var freshPlayer = await CreateFreshPlayerAsync("WT_Normal");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@warnings me=normal"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "Warnings set to: normal"), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async Task WarningsCommand_SetToAll()
	{
		// ParseWarnings("all") → WarningType.All; UnparseWarnings → "all".
		var freshPlayer = await CreateFreshPlayerAsync("WT_All");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@warnings me=all"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "Warnings set to: all"), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async Task WarningsCommand_SetToNone()
	{
		// ParseWarnings("none") → WarningType.None → "Warnings cleared." branch.
		var freshPlayer = await CreateFreshPlayerAsync("WT_None");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@warnings me=none"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "Warnings cleared."), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async Task WarningsCommand_WithNegation()
	{
		// ParseWarnings("all !exit-desc") → All & ~ExitDesc = Extra; UnparseWarnings → "extra".
		var freshPlayer = await CreateFreshPlayerAsync("WT_Negate");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@warnings me=all !exit-desc"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "Warnings set to: extra"), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async Task WarningsCommand_WithUnknownWarning()
	{
		// @warnings sends "Unknown warning: unknown-warning" for each unrecognised token.
		var freshPlayer = await CreateFreshPlayerAsync("WT_Unknown");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@warnings me=unknown-warning"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "Unknown warning: unknown-warning"), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async Task WarningsCommand_NoArguments_ShowsUsage()
	{
		// @warnings with no object arg sends "Usage: @warnings <object>=<warning list>" as the first line.
		var freshPlayer = await CreateFreshPlayerAsync("WT_Usage");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@warnings"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "Usage: @warnings <object>=<warning list>"), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async Task WCheckCommand_SpecificObject()
	{
		// Fresh player checks their own DBRef (they own themselves → permission passes).
		var freshPlayer = await CreateFreshPlayerAsync("WT_WCheck");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService,
			MarkupText.Plain($"@wcheck #{freshPlayer.DbRef.Number}"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "@wcheck complete."), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async Task WCheckCommand_NoArguments_ShowsUsage()
	{
		var freshPlayer = await CreateFreshPlayerAsync("WT_WCheckUsage");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@wcheck"));

		await Assert.That(NotifyService.Recorded().Count(freshPlayer.DbRef, s => TestHelpers.MessagePlainTextEquals(s, "Usage: @wcheck <object> or @wcheck/me or @wcheck/all"), sender: freshPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Integration test - requires proper object setup")]
	public async Task WCheckCommand_WithMe_ChecksOwnedObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@wcheck/me"));

		await Assert.That(NotifyService.Recorded().Count(executor, s => TestHelpers.MessagePlainTextEquals(s, "Checking objects you own..."), sender: executor, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}
}
