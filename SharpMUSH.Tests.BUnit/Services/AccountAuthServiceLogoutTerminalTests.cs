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
/// routes through, so the teardown belongs here where no caller can miss it. A bare socket close is
/// not enough — the ConnectionServer treats it as a droppable disconnect and holds the session for a
/// grace window, so the character lingers in lwho(). Sending the game <c>QUIT</c> command forces the
/// server to fully end the session (immediate registry removal + a bye frame that suppresses auto-
/// reconnect); the follow-up <c>DisconnectAsync</c> closes the socket and latches the intentional-
/// disconnect flag. Both are guarded on <c>IsConnected</c> so an idle terminal is a no-op.
/// </summary>
public class AccountAuthServiceLogoutTerminalTests : BunitContext
{
	private AccountAuthService CreateService(ITerminalService terminal, IPlayTerminalService playTerminal)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		return new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance,
			terminal,
			playTerminal);
	}

	[TUnit.Core.Test]
	public async Task LogoutAsync_ConnectedTerminals_QuitsAndDisconnectsBoth()
	{
		var terminal = Substitute.For<ITerminalService>();
		var playTerminal = Substitute.For<IPlayTerminalService>();
		terminal.IsConnected.Returns(true);
		playTerminal.IsConnected.Returns(true);

		var service = CreateService(terminal, playTerminal);

		await service.LogoutAsync();

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

		var service = CreateService(terminal, playTerminal);

		await service.LogoutAsync();

		await terminal.DidNotReceive().SendAsync(Arg.Any<string>());
		await terminal.DidNotReceive().DisconnectAsync();
		await playTerminal.DidNotReceive().SendAsync(Arg.Any<string>());
		await playTerminal.DidNotReceive().DisconnectAsync();
	}
}
