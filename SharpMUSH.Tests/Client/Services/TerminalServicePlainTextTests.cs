using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>Raw server text (a banner, text from before markup frames) reaches the scrollback decoded.</summary>
public class TerminalServicePlainTextTests
{
	private static (TerminalService Svc, IWebSocketClientService Ws) NewService()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		return (new TerminalService(ws, Substitute.For<ILogger<TerminalService>>()), ws);
	}

	[Test]
	public async Task PlainFrameIsShownWithItsStyling()
	{
		var (svc, ws) = NewService();
		await svc.ConnectAsync("ws://localhost:4202/ws");

		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws, "\u001b[1mWelcome\u001b[0m\r\n\r\nType connect.\r\n");

		var server = svc.Lines.Where(l => l.Source == TerminalLineSource.Server).ToArray();
		await Assert.That(server.Select(l => l.Text)).IsEquivalentTo(["Welcome", "Type connect."], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(server[0].Html).IsEqualTo("<span class=\"ms-bold\">Welcome</span>");
		await Assert.That(server[1].Html).IsEqualTo("Type connect.");
	}

	/// <summary>A reply to a terminal query would be run by the game as a command, so none is sent.</summary>
	[Test]
	public async Task TerminalQueryIsNotAnswered()
	{
		var (svc, ws) = NewService();
		await svc.ConnectAsync("ws://localhost:4202/ws");
		ws.ClearReceivedCalls();

		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws, "Hello\u001b[c\u001b[18t\n");

		await Assert.That(svc.Lines.Any(l => l.Source == TerminalLineSource.Server && l.Text == "Hello")).IsTrue();
		await ws.DidNotReceive().SendAsync(Arg.Any<string>());
	}
}
