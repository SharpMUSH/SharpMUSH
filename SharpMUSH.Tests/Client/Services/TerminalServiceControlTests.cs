using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class TerminalServiceControlTests
{
	[Test]
	public async Task SendControlAsync_SendsRaw_AndDoesNotAddLine()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		var svc = new TerminalService(ws, logger);

		await svc.SendControlAsync("{\"type\":\"naws\",\"cols\":80,\"rows\":24}");

		await ws.Received(1).SendAsync("{\"type\":\"naws\",\"cols\":80,\"rows\":24}");
		await Assert.That(svc.Lines.Count).IsEqualTo(0);
	}

	[Test]
	public async Task ConnectAsGuestAsync_Connects_ClearsBuffer_AndSendsConnectGuest()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		var svc = new TerminalService(ws, logger);

		await svc.ConnectAsGuestAsync("ws://localhost:4202/ws");

		ws.Received(1).ClearSendBuffer();
		await ws.Received(1).ConnectAsync("ws://localhost:4202/ws");
		await ws.Received(1).SendAsync("connect guest");
	}
}
