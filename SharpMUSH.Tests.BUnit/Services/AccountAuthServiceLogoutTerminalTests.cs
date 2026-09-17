using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Logging out of the portal must also end the game-side session. When a character session is
/// active the terminals are connected to the ConnectionServer over a WebSocket, and until the
/// character leaves the live registry it keeps showing as online (lwho()).
/// <see cref="AccountAuthService.LogoutAsync"/> is the single chokepoint every logout entry point
/// routes through, so the teardown runs from there — but it is now the terminal layer's own
/// <see cref="TerminalSessionTeardown"/>, registered as an
/// <see cref="IAccountSessionEndingHandler"/>, rather than the auth service reaching down into it.
/// <para>
/// A bare socket close is not enough — the ConnectionServer treats it as a droppable disconnect and
/// holds the session for a grace window, so the character lingers in lwho(). Sending the game
/// <c>QUIT</c> command forces the server to fully end the session (immediate registry removal plus a
/// bye frame that suppresses auto-reconnect); the follow-up <c>DisconnectAsync</c> closes the socket
/// and latches the intentional-disconnect flag. Both are guarded on <c>IsConnected</c> so an idle
/// terminal is a no-op.
/// </para>
/// </summary>
public class AccountAuthServiceLogoutTerminalTests : BunitContext
{
	private AccountAuthService CreateService(params IAccountSessionEndingHandler[] handlers)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		return new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance,
			handlers);
	}

	[TUnit.Core.Test]
	public async Task LogoutAsync_ConnectedTerminals_QuitsAndDisconnectsBoth()
	{
		var terminal = Substitute.For<ITerminalService>();
		var playTerminal = Substitute.For<IPlayTerminalService>();
		terminal.IsConnected.Returns(true);
		playTerminal.IsConnected.Returns(true);

		await CreateService(new TerminalSessionTeardown(terminal, playTerminal)).LogoutAsync();

		await terminal.Received(1).SendAsync("QUIT");
		await terminal.Received(1).DisconnectAsync();
		await playTerminal.Received(1).SendAsync("QUIT");
		await playTerminal.Received(1).DisconnectAsync();
	}

	[TUnit.Core.Test]
	public async Task LogoutAsync_IdleTerminals_DoesNotQuitOrDisconnect()
	{
		var terminal = Substitute.For<ITerminalService>();
		var playTerminal = Substitute.For<IPlayTerminalService>();
		terminal.IsConnected.Returns(false);
		playTerminal.IsConnected.Returns(false);

		await CreateService(new TerminalSessionTeardown(terminal, playTerminal)).LogoutAsync();

		await terminal.DidNotReceive().SendAsync(Arg.Any<string>());
		await terminal.DidNotReceive().DisconnectAsync();
		await playTerminal.DidNotReceive().SendAsync(Arg.Any<string>());
		await playTerminal.DidNotReceive().DisconnectAsync();
	}

	/// <summary>
	/// A cleanup that fails must not leave the tab signed in. The alternative to logging and carrying
	/// on is a tab that failed to hang up a socket and still holds a session token.
	/// </summary>
	[TUnit.Core.Test]
	public async Task LogoutAsync_AHandlerThatThrows_StillEndsTheSession()
	{
		var faulty = Substitute.For<IAccountSessionEndingHandler>();
		faulty.OnAccountSessionEndingAsync().Returns(Task.FromException(new InvalidOperationException("boom")));
		var terminal = Substitute.For<ITerminalService>();
		var playTerminal = Substitute.For<IPlayTerminalService>();
		terminal.IsConnected.Returns(true);
		playTerminal.IsConnected.Returns(true);

		var service = CreateService(faulty, new TerminalSessionTeardown(terminal, playTerminal));

		await service.LogoutAsync();

		await Assert.That(service.AccountSessionToken).IsNull();
		await Assert.That(service.ExplicitlyLoggedOut).IsTrue();
		// One handler failing must not skip the ones after it.
		await terminal.Received(1).SendAsync("QUIT");
	}
}
