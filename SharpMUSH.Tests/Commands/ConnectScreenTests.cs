using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The commands a telnet player can run before logging in. <c>WHO</c> and <c>QUIT</c> are
/// <c>CommandBehavior.SOCKET</c> precisely so they answer at the connect screen, but both opened
/// with an unconditional <c>KnownExecutorObject()</c>, which throws
/// <see cref="ArgumentNullException"/> because a handle at the connect screen has no executor.
/// </summary>
public class ConnectScreenTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<long> AnonymousHandleAsync()
		=> await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);

	[Test]
	public async Task WhoAtTheConnectScreenListsPlayersInsteadOfThrowing()
	{
		await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WhoVisible");

		var anonymous = await AnonymousHandleAsync();
		await Parser.CommandParse(anonymous, ConnectionService, MarkupText.Plain("WHO"));

		var listing = LastNotificationTo(anonymous);

		await Assert.That(listing).IsNotNull();
		await Assert.That(listing!).DoesNotStartWith("#-1 EXCEPTION: ");
		await Assert.That(listing).Contains("WhoVisible");
		await Assert.That(listing).Contains("player");
	}

	/// <summary>
	/// A WHO crawler reads one line per player, so a long <c>@doing</c> is cut to PennMUSH's 40
	/// characters on the player's own line rather than wrapped onto the next.
	/// </summary>
	[Test]
	public async Task WhoKeepsEachPlayerOnOneLine()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WhoOneLine");
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("&DOING me=alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo"));

		var anonymous = await AnonymousHandleAsync();
		await Parser.CommandParse(anonymous, ConnectionService, MarkupText.Plain("WHO"));

		var lines = LastNotificationTo(anonymous)!.Split('\n');
		var row = Array.FindIndex(lines, line => line.StartsWith(player.Name, StringComparison.Ordinal));

		await Assert.That(row).IsGreaterThan(0);
		await Assert.That(lines[row]).EndsWith("alpha bravo charlie delta echo foxtrot");
		await Assert.That(lines[row + 1]).DoesNotStartWith(" ");
	}

	/// <summary>
	/// An anonymous viewer must get the mortal layout. The wizard layout carries host names and
	/// descriptors, which is exactly what a nobody at the connect screen must not be handed.
	/// </summary>
	[Test]
	public async Task WhoAtTheConnectScreenUsesTheMortalHeaderNotTheWizardOne()
	{
		var anonymous = await AnonymousHandleAsync();
		await Parser.CommandParse(anonymous, ConnectionService, MarkupText.Plain("WHO"));

		var header = LastNotificationTo(anonymous)!.Split('\n')[0];

		await Assert.That(header).Contains("Player Name");
		await Assert.That(header).Contains("Doing");
		await Assert.That(header).DoesNotContain("Host");
		await Assert.That(header).DoesNotContain("Loc #");
		await Assert.That(header).DoesNotContain("Des");
	}

	/// <summary>
	/// QUIT threw at its second statement, before <c>Disconnect()</c> and the
	/// <c>DisconnectConnectionMessage</c> — so the connection also survived a QUIT it had just
	/// refused to acknowledge.
	/// </summary>
	[Test]
	public async Task QuitAtTheConnectScreenSaysGoodbyeAndDropsTheConnection()
	{
		var anonymous = await AnonymousHandleAsync();
		await Parser.CommandParse(anonymous, ConnectionService, MarkupText.Plain("QUIT"));

		var messages = NotificationsTo(WebAppFactoryArg, anonymous);

		await Assert.That(messages).Contains("GOODBYE.");
		await Assert.That(messages.Any(m => m.StartsWith("#-1 EXCEPTION: "))).IsFalse();
		await Assert.That(ConnectionService.Get(anonymous)).IsNull();
	}

	private string? LastNotificationTo(long handle) =>
		NotificationsTo(WebAppFactoryArg, handle).LastOrDefault();

	/// <summary>
	/// Plain text of everything sent to connection <paramref name="handle"/>, in order. The factory's
	/// recorder indexes handle-addressed output, so this does not scan the session-shared substitute's
	/// whole call list.
	/// </summary>
	internal static string[] NotificationsTo(ServerWebAppFactory factory, long handle) =>
		[.. factory.Notifications.ForHandle(handle)];
}
