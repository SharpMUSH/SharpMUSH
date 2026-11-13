using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

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

		await Assert.That(messages.Any(m => m.Contains("Come back soon", StringComparison.Ordinal))).IsTrue();
		await Assert.That(messages.Any(m => m.StartsWith("#-1 EXCEPTION: "))).IsFalse();
		await Assert.That(ConnectionService.Get(anonymous)).IsNull();
	}

	/// <summary>
	/// A character's other connections stay in the game when one of them quits. Only the leaving one
	/// gets the goodbye (PennMUSH's disconnect_player sends the quit file to that descriptor alone);
	/// the others are told they partially disconnected. The goodbye was once sent to the player, which
	/// reached every connection the character had.
	/// </summary>
	[Test]
	public async Task QuitSaysGoodbyeOnlyToTheLeavingConnection()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "QuitOneOfTwo");
		var staying = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player.DbRef);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("QUIT"));

		await Assert.That(() => NotificationsTo(WebAppFactoryArg, staying))
			.WaitsFor(lines => lines.Contains(ErrorMessages.Notifications.YouHavePartiallyDisconnected),
				timeout: TimeSpan.FromSeconds(5), pollingInterval: TimeSpan.FromMilliseconds(50));

		var leaving = NotificationsTo(WebAppFactoryArg, player.Handle);
		await Assert.That(leaving.Count(m => m.Contains("Come back soon", StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(leaving).DoesNotContain("GOODBYE.");
		await Assert.That(NotificationsTo(WebAppFactoryArg, staying).Any(m => m.Contains("Come back soon", StringComparison.Ordinal))).IsFalse();
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef).Any(m => m.Contains("Come back soon", StringComparison.Ordinal))).IsFalse();
		await Assert.That(ConnectionService.Get(staying)?.Ref).IsEqualTo(player.DbRef);
	}

	/// <summary>
	/// A client that goes away without QUIT (a closed browser tab, whose session the socket server holds for a
	/// while first) tells the character's other connections nothing. A QUIT after it still does, once.
	/// </summary>
	[Test]
	public async Task ClosedSocketTellsNoOtherConnectionItPartiallyDisconnected()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropOneOfThree");
		var quitting = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player.DbRef);
		var staying = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player.DbRef);

		await new ConnectionClosedConsumer(NullLogger<ConnectionClosedConsumer>.Instance, ConnectionService)
			.HandleAsync(new ConnectionClosedMessage(player.Handle, DateTimeOffset.UtcNow));
		await Parser.CommandParse(quitting, ConnectionService, MarkupText.Plain("QUIT"));

		await Assert.That(() => NotificationsTo(WebAppFactoryArg, staying))
			.WaitsFor(lines => lines.Contains(ErrorMessages.Notifications.YouHavePartiallyDisconnected),
				timeout: TimeSpan.FromSeconds(5), pollingInterval: TimeSpan.FromMilliseconds(50));

		await Assert.That(ConnectionService.Get(player.Handle)).IsNull();
		await Assert.That(NotificationsTo(WebAppFactoryArg, staying)
			.Count(m => m == ErrorMessages.Notifications.YouHavePartiallyDisconnected)).IsEqualTo(1);
		await Assert.That(NotificationsTo(WebAppFactoryArg, quitting)).DoesNotContain(ErrorMessages.Notifications.YouHavePartiallyDisconnected);
	}

	/// <summary>
	/// <c>@kick</c> takes every connection, one at a time. None of them is told the character partially
	/// disconnected on the way, since none is staying.
	/// </summary>
	[Test]
	public async Task KickTellsNoConnectionItPartiallyDisconnected()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "KickBoth");
		var second = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player.DbRef);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@kick {player.DbRef}"));

		await Assert.That(ConnectionService.Get(player.Handle)).IsNull();
		await Assert.That(ConnectionService.Get(second)).IsNull();
		await Assert.That(NotificationsTo(WebAppFactoryArg, player.Handle)).DoesNotContain(ErrorMessages.Notifications.YouHavePartiallyDisconnected);
		await Assert.That(NotificationsTo(WebAppFactoryArg, second)).DoesNotContain(ErrorMessages.Notifications.YouHavePartiallyDisconnected);
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
