using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using MarkupString;
using SharpMUSH.Library.Markup;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class LifecycleNoticeTests
{
	private static EngineLifecycleNoticeService Notices(IConnectionServerService service, IMarkupOutputRenderer? renderer = null) =>
		new(service, renderer ?? new MarkupOutputRenderer(), new OutputTransformService(),
			NullLogger<EngineLifecycleNoticeService>.Instance);

	private static async Task<List<byte[]>> NoticesSentTo(string connectionType, ProtocolCapabilities? capabilities = null,
		IMarkupOutputRenderer? renderer = null, Func<Encoding>? encoding = null, int sockets = 1)
	{
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, Substitute.For<IMessageBus>());
		var output = new List<byte[]>();
		for (var handle = 1; handle <= sockets; handle++)
		{
			// The service keeps one write gate per writer, so each socket needs a writer of its own: capturing the
			// loop's handle keeps the compiler from sharing one delegate between them.
			var socket = handle;
			await service.RegisterAsync(handle, "localhost", "localhost", connectionType,
				data => { _ = socket; lock (output) output.Add(data); return ValueTask.CompletedTask; },
				_ => ValueTask.CompletedTask,
				encoding ?? (() => Encoding.UTF8), () => { }, capabilities: capabilities);
		}
		var notices = Notices(service, renderer);
		var now = DateTimeOffset.UtcNow;
		await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
		await notices.NotifyAsync(now.AddSeconds(1), true).WaitAsync(TimeSpan.FromSeconds(5));
		return output;
	}

	/// <summary>
	/// A notice is one line: the telnet layer ends each write, so a notice that brought line breaks of its own
	/// left blank lines around it.
	/// </summary>
	[Test]
	public async Task TerminalNoticesAreOneLineEach()
	{
		var output = await NoticesSentTo("telnet", new ProtocolCapabilities(Format: OutputFormat.Ansi));
		var lines = output.Select(Encoding.UTF8.GetString).ToList();

		await Assert.That(lines.Count).IsEqualTo(2);
		foreach (var line in lines)
		{
			await Assert.That(line).DoesNotContain("\r");
			await Assert.That(line).DoesNotContain("\n");
		}
		await Assert.That(StripAnsi(lines[0])).IsEqualTo("[GAME] " + EngineLifecycleNoticeService.Restarting);
		await Assert.That(StripAnsi(lines[1])).IsEqualTo("[GAME] " + EngineLifecycleNoticeService.Ready);
	}

	/// <summary>The notices are styled as the engine's own notices are: the <c>[GAME]</c> badge, bold.</summary>
	[Test]
	public async Task TerminalNoticesCarryTheNoticeBadgeStyling()
	{
		var output = await NoticesSentTo("telnet", new ProtocolCapabilities(Format: OutputFormat.Ansi));
		var expected = new MarkupOutputRenderer().Render(
			MarkupTextSerializer.Serialize(NoticeMarkup.Message("GAME", EngineLifecycleNoticeService.Ready)),
			Connection("telnet")).Data;

		await Assert.That(Encoding.UTF8.GetString(output[1])).IsEqualTo(Encoding.UTF8.GetString(expected));
		await Assert.That(Encoding.UTF8.GetString(output[1])).StartsWith("\u001b[");
	}

	/// <summary>A browser gets a markup frame, as the engine's output is sent, and draws the badge itself.</summary>
	[Test]
	public async Task WebSocketNoticesAreMarkupFrames()
	{
		var output = await NoticesSentTo("websocket");

		await Assert.That(output.Count).IsEqualTo(2);
		await Assert.That(Encoding.UTF8.GetString(output[0])).IsEqualTo(Encoding.UTF8.GetString(
			WebSocketMarkupFrames.Line(MarkupTextSerializer.Serialize(EngineLifecycleNoticeService.Notice(false)))));
		await Assert.That(Encoding.UTF8.GetString(output[1])).IsEqualTo(Encoding.UTF8.GetString(
			WebSocketMarkupFrames.Line(MarkupTextSerializer.Serialize(EngineLifecycleNoticeService.Notice(true)))));
	}

	/// <summary>
	/// A whole-stack update can take the rendering worker down with the engine; the notice still goes out, as
	/// plain text in the connection's own encoding.
	/// </summary>
	[Test]
	public async Task TerminalNoticesFallBackToPlainTextWhenTheWorkerIsDown()
	{
		var renderer = Substitute.For<IMarkupOutputRenderer>();
		renderer.RenderAsync(Arg.Any<string>(), Arg.Any<ConnectionServerService.ConnectionData>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromException<RenderedOutput>(new HttpRequestException("worker down")));

		var output = await NoticesSentTo("telnet", renderer: renderer, encoding: () => Encoding.ASCII);

		await Assert.That(output.Select(Encoding.ASCII.GetString).ToList()).IsEquivalentTo(new[]
		{
			"[GAME] " + EngineLifecycleNoticeService.Restarting,
			"[GAME] " + EngineLifecycleNoticeService.Ready
		});
	}

	/// <summary>
	/// The real worker client never fails while the worker is down: it waits for it. A notice gives it one second,
	/// once for the whole broadcast, so a full house of sockets is not each made to wait that second.
	/// </summary>
	[Test]
	public async Task AnUnansweringWorkerIsWaitedOnOncePerNotice()
	{
		var renderer = Substitute.For<IMarkupOutputRenderer>();
		renderer.RenderAsync(Arg.Any<string>(), Arg.Any<ConnectionServerService.ConnectionData>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => new ValueTask<RenderedOutput>(Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>())
				.ContinueWith<RenderedOutput>(_ => throw new OperationCanceledException(), TaskScheduler.Default)));

		var output = await NoticesSentTo("telnet", new ProtocolCapabilities(Format: OutputFormat.Ansi), renderer, sockets: 64);

		await Assert.That(output.Count).IsEqualTo(128);
		await Assert.That(output.Select(Encoding.UTF8.GetString).Distinct().ToList()).IsEquivalentTo(new[]
		{
			"[GAME] " + EngineLifecycleNoticeService.Restarting,
			"[GAME] " + EngineLifecycleNoticeService.Ready
		});
		await renderer.ReceivedWithAnyArgs(2).RenderAsync(default!, default!, default, default);
	}

	/// <summary>The plain text keeps what the worker would have done for the client: a Pueblo client's line break.</summary>
	[Test]
	public async Task PlainFallbackEndsAPuebloLine()
	{
		var renderer = Substitute.For<IMarkupOutputRenderer>();
		renderer.RenderAsync(Arg.Any<string>(), Arg.Any<ConnectionServerService.ConnectionData>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromException<RenderedOutput>(new HttpRequestException("worker down")));

		var output = await NoticesSentTo("telnet", new ProtocolCapabilities(Format: OutputFormat.Pueblo), renderer);

		await Assert.That(Encoding.UTF8.GetString(output[1])).IsEqualTo("[GAME] " + EngineLifecycleNoticeService.Ready + "<BR>");
	}

	private static string StripAnsi(string text) => System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-9;]*m", string.Empty);

	private static ConnectionServerService.ConnectionData Connection(string connectionType) =>
		new(1, null, ConnectionServerService.ConnectionState.Connected, _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { }, null,
			new ProtocolCapabilities(Format: OutputFormat.Ansi), null, connectionType);

	[Test]
	public async Task NoticesReachEverySocketWithoutDisconnectingAndIgnoreOldEvents()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var output = new List<string>();
		var disconnected = false;
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			_ => throw new IOException("broken socket"), _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => disconnected = true);
		await service.RegisterAsync(2, "localhost", "localhost", "websocket",
			data => { output.Add(Encoding.UTF8.GetString(data)); return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => disconnected = true);
		var notices = Notices(service);
		var now = DateTimeOffset.UtcNow;
		await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
		await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
		await notices.NotifyAsync(now.AddSeconds(1), true).WaitAsync(TimeSpan.FromSeconds(5));
		await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(output.Count).IsEqualTo(2);
		await Assert.That(output[0]).Contains("restarting");
		await Assert.That(output[1]).Contains("ready");
		await Assert.That(disconnected).IsFalse();
	}

	[Test]
	public async Task DisconnectClosesSocketEvenWhenBusFails()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var disconnected = false;
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => disconnected = true);
		bus.Publish(Arg.Any<SharpMUSH.Messaging.Messages.ConnectionClosedMessage>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new IOException("bus unavailable")));
		await service.DisconnectAsync(1);
		await Assert.That(disconnected).IsTrue();
		await Assert.That(service.Get(1)).IsNull();
	}
	[Test]
	public async Task InputFailureKeepsSocketAndDoesNotRetryAnUncertainCommand()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var output = new List<string>();
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			data => { output.Add(Encoding.UTF8.GetString(data)); return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => throw new Exception("Must not disconnect"));
		var message = new SharpMUSH.Messaging.Messages.TelnetInputMessage(1, "look");
		bus.Publish(message, Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException()));
		await ConnectionInputPublisher.PublishAsync(bus, service, NullLogger.Instance, 1, message, default);
		await bus.Received(1).Publish(message, Arg.Any<CancellationToken>());
		await Assert.That(output.Single()).Contains("could not be confirmed");
		await Assert.That(service.Get(1)).IsNotNull();
	}

	[Test]
	public async Task RefreshContinuesAfterStoreFailureWithoutReplacingBindings()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		foreach (var handle in new long[] { 1, 2 })
			await service.RegisterAsync(handle, "localhost", "localhost", "telnet",
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
				() => throw new Exception("Must not disconnect"));
		var store = Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionStateStore>();
		store.UpdateMetadataAsync(1, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new IOException()));
		await new ConnectionStateRefreshService(service, store,
			NullLogger<ConnectionStateRefreshService>.Instance).RefreshAsync(default);
		await store.Received(1).UpdateMetadataAsync(2, "GatewayLastSeen", Arg.Any<string>(), Arg.Any<CancellationToken>());
		await store.DidNotReceive().SetConnectionAsync(Arg.Any<long>(),
			Arg.Any<SharpMUSH.Library.Services.Interfaces.ConnectionStateData>(), Arg.Any<CancellationToken>());
		await Assert.That(service.GetAll().Count()).IsEqualTo(2);
	}

	[Test]
	public async Task TimedOutNoticeDoesNotOverlapTheNextNotice()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = 0;
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			_ => { writes++; return new ValueTask(pending.Task); }, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => { });
		var notices = Notices(service);
		var now = DateTimeOffset.UtcNow;
		try
		{
			await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
			await notices.NotifyAsync(now.AddSeconds(1), true).WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(writes).IsEqualTo(1);
		}
		finally { pending.TrySetResult(); }
	}

}
