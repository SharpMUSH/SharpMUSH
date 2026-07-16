using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Covers <see cref="TerminalService.DisposeAsync"/> — the disposal half of the recreate-not-reconnect
/// fix for character switching. A fresh <see cref="IWebSocketClientService"/> built after disposal has
/// a null resume token, so it sends hello instead of resume and cannot rebind to the wrong session.
/// </summary>
public class TerminalServiceDisposalTests
{
	[Test]
	public async Task DisposeAsync_disposes_the_websocket_client()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var sut = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>());

		await sut.DisposeAsync();

		await ws.Received(1).DisposeAsync();
	}

	[Test]
	public async Task DisposeAsync_clears_LineReceived_subscribers()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var sut = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>());
		var fired = 0;
		sut.LineReceived += _ => fired++;

		await sut.DisposeAsync();
		// AddSystemLine is private; DisconnectAsync is the public path that reaches it
		// (it appends an "Disconnected." system line after tearing down the connection).
		await sut.DisconnectAsync();

		await Assert.That(fired).IsEqualTo(0);
	}

	[Test]
	public async Task DisposeAsync_is_idempotent()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var sut = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>());

		await sut.DisposeAsync();
		await sut.DisposeAsync();

		await ws.Received(1).DisposeAsync();
	}
}
