using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class TerminalServiceOobTests
{
	[Test]
	public async Task IncomingOobFrameIsRoutedToStore()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		var svc = new TerminalService(ws, logger);

		// Drive a message through the ws MessageReceived event after connecting wires handlers.
		await svc.ConnectAsync("ws://localhost:4202/ws");
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws,
			"{\"type\":\"oob\",\"package\":\"room.contents\",\"data\":{\"who\":[\"#7\"]}}");

		await Assert.That(svc.OobChannels.Get("room.contents")).IsEqualTo("{\"who\":[\"#7\"]}");
		await Assert.That(svc.Lines.Any(l => l.Text.Contains("room.contents"))).IsFalse();
	}
}
