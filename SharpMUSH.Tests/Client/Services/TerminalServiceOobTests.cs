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

	[Test]
	public async Task OrphanedEndMarkerIsNotDisplayed()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		var svc = new TerminalService(ws, logger);

		await svc.ConnectAsync("ws://localhost:4202/ws");

		// No SendCommandAsync is in flight, so this end-marker has no matching pending request.
		// It must still be swallowed instead of leaking into the visible buffer.
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws, "SHARP_END:deadbeef");

		await Assert.That(svc.Lines.Any(l => l.Text.Contains("SHARP_END"))).IsFalse();
	}

	[Test]
	public async Task NonMarkerServerLineIsStillDisplayed()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		var svc = new TerminalService(ws, logger);

		await svc.ConnectAsync("ws://localhost:4202/ws");

		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws, "You see a small room.");

		await Assert.That(svc.Lines.Any(l => l.Text.Contains("You see a small room."))).IsTrue();
	}
}
