using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class TerminalServiceOobTests
{
	private static (TerminalService Svc, IWebSocketClientService Ws) NewService()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		return (new TerminalService(ws, logger), ws);
	}

	[Test]
	public async Task IncomingOobFrameIsRoutedToStore()
	{
		var (svc, ws) = NewService();

		await svc.ConnectAsync("ws://localhost:4202/ws");
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws,
			"{\"type\":\"oob\",\"package\":\"room.contents\",\"data\":{\"who\":[\"#7\"]}}");

		await Assert.That(svc.OobChannels.Get("room.contents")).IsEqualTo("{\"who\":[\"#7\"]}");
		await Assert.That(svc.Lines.Any(l => l.Text.Contains("room.contents"))).IsFalse();
	}

	[Test]
	public async Task SendCommandAsync_WrapsExpressionInSelfTargetedOobEnvelope()
	{
		var (svc, ws) = NewService();
		await svc.ConnectAsync("ws://localhost:4202/ws");

		string? sent = null;
		ws.When(w => w.SendAsync(Arg.Any<string>())).Do(ci => sent = ci.Arg<string>());

		// 100 ms timeout: no OOB reply arrives, so the call returns [] once the timer fires.
		var result = await svc.SendCommandAsync("lcon(me)", 100);

		await Assert.That(result).IsEmpty();
		await Assert.That(sent).IsNotNull();
		await Assert.That(sent!).Contains("think [null(oob(me, query.");
		await Assert.That(sent!).Contains("json(string, lcon(me))");
	}

	[Test]
	public async Task QueryCompletesFromOobFrameAndNeverLeaksToVisibleBuffer()
	{
		var (svc, ws) = NewService();
		await svc.ConnectAsync("ws://localhost:4202/ws");

		string? reqId = null;
		ws.When(w => w.SendAsync(Arg.Any<string>())).Do(ci =>
		{
			var match = Regex.Match(ci.Arg<string>(), @"query\.([0-9a-f]+)");
			if (match.Success) reqId = match.Groups[1].Value;
		});

		// SendAsync completes synchronously in the mock, so by the time SendCommandAsync yields on
		// its completion Task the request id has already been captured and the pending slot is armed.
		var task = svc.SendCommandAsync("lcon(me)");
		await Assert.That(reqId).IsNotNull();

		// The engine's oob(me, query.<id>, json(string, <value>)) arrives as a flat OOB envelope
		// whose data is a JSON string; the two dbrefs are separated by a real newline.
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws,
			$"{{\"type\":\"oob\",\"package\":\"query.{reqId}\",\"data\":\"1000002\\n1000003\"}}");

		var result = await task;

		await Assert.That(result).IsEquivalentTo(new[] { "1000002", "1000003" });
		await Assert.That(svc.Lines.Any(l => l.Text.Contains("1000002"))).IsFalse();
		await Assert.That(svc.Lines.Any(l => l.Text.Contains("query."))).IsFalse();
		await Assert.That(svc.OobChannels.Packages.Contains($"query.{reqId}")).IsFalse();
	}

	[Test]
	public async Task QueryOobWithScalarJsonDataStillCompletes()
	{
		var (svc, ws) = NewService();
		await svc.ConnectAsync("ws://localhost:4202/ws");

		string? reqId = null;
		ws.When(w => w.SendAsync(Arg.Any<string>())).Do(ci =>
		{
			var match = Regex.Match(ci.Arg<string>(), @"query\.([0-9a-f]+)");
			if (match.Success) reqId = match.Groups[1].Value;
		});

		var task = svc.SendCommandAsync("hasflag(me,WIZARD)");
		await Assert.That(reqId).IsNotNull();

		// json(string, 1) yields the JSON string "1"; a scalar (non-string) data value must also work.
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws,
			$"{{\"type\":\"oob\",\"package\":\"query.{reqId}\",\"data\":1}}");

		var result = await task;
		await Assert.That(result).IsEquivalentTo(new[] { "1" });
	}

	[Test]
	public async Task OrphanedQueryOobFrameIsNeitherDisplayedNorStored()
	{
		var (svc, ws) = NewService();
		await svc.ConnectAsync("ws://localhost:4202/ws");

		// No SendCommandAsync is in flight, so this correlation frame has no matching pending request.
		// It must be swallowed: never shown in the buffer and never stored as a channel payload.
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws,
			"{\"type\":\"oob\",\"package\":\"query.deadbeef\",\"data\":\"leak\"}");

		await Assert.That(svc.Lines.Any(l => l.Text.Contains("leak"))).IsFalse();
		await Assert.That(svc.OobChannels.Packages.Contains("query.deadbeef")).IsFalse();
	}

	[Test]
	public async Task NonMarkerServerLineIsStillDisplayed()
	{
		var (svc, ws) = NewService();
		await svc.ConnectAsync("ws://localhost:4202/ws");

		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws, "You see a small room.");

		await Assert.That(svc.Lines.Any(l => l.Text.Contains("You see a small room."))).IsTrue();
	}
}
