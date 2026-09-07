using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Configuration;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Tests.ConnectionServer.TestSchedulers;

namespace SharpMUSH.Tests.ConnectionServer;

public class ConnectionRecoveryDeadlineTests
{
	[Test]
	public async Task StalledOutputDetachesButPreservesReplayAndReleasesGate()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var replay = new TerminalReplayStore();
		var sinks = new SessionSinkRegistry();
		var transport = new Transport("{\"type\":\"hello\"}") { StallOutput = true };
		var pump = Pump(service, bus, replay, new ResumeTokenService(), sinks);
		var running = pump.RunAsync(transport, 42, default);
		await transport.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var sink = sinks.Get(42)!;
		await service.Get(42)!.OutputFunction("buffered"u8.ToArray()).AsTask().WaitAsync(TimeSpan.FromSeconds(8));
		await running.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(sink.Current).IsNull();
		await Assert.That(sink.OutputGate.CurrentCount).IsEqualTo(1);
		await Assert.That((await replay.AfterAsync(sink.SessionId, 0)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task ExplicitDisconnectPurgesReplayAndLateOutputCannotRecreateIt()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var replay = new TerminalReplayStore();
		var sinks = new SessionSinkRegistry();
		var transport = new Transport("{\"type\":\"hello\"}");
		var running = Pump(service, bus, replay, new ResumeTokenService(), sinks).RunAsync(transport, 42, default);
		await transport.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var sink = sinks.Get(42)!;
		var output = service.Get(42)!.OutputFunction;
		await output("before"u8.ToArray());
		await service.DisconnectAsync(42);
		await running.WaitAsync(TimeSpan.FromSeconds(5));
		await output("late"u8.ToArray());
		await Assert.That((await replay.AfterAsync(sink.SessionId, 0)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task IncompleteReplayFallsBackToFreshSessionWithoutOldFrames()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var replay = Substitute.For<ITerminalReplayStore>();
		replay.ReadAsync("session", 0, Arg.Any<CancellationToken>()).Returns(new ReplayReadResult(false, []));
		var tokens = new ResumeTokenService();
		var pump = Pump(service, bus, replay, tokens, new SessionSinkRegistry());
		await pump.RestoreDormantAsync(new ConnectionStateData
		{
			Handle = 9, State = "LoggedIn", PlayerObjid = "#7:1000", IpAddress = "old", Hostname = "old",
			ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UtcNow,
			Metadata = new() { ["SessionId"] = "session" }
		}, DateTimeOffset.UtcNow.AddMinutes(1), default);
		var token = await tokens.MintAsync(9, "session");
		var transport = new Transport($"{{\"type\":\"resume\",\"token\":\"{token}\",\"lastSeq\":0}}") { EndAfterHello = true };
		await pump.RunAsync(transport, 42, default);
		await Assert.That(transport.Sent.Any(frame => frame.Contains("reattached"))).IsFalse();
		await Assert.That(service.Get(42)).IsNotNull();
		await Assert.That((await tokens.TryResolveAsync(token)).Found).IsFalse();
	}

	[Test]
	public async Task BrokerOutageCannotRetainLocalDisconnect()
	{
		var bus = Substitute.For<IMessageBus>();
		var state = Substitute.For<IConnectionStateStore>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, state);
		var closed = false;
		await service.RegisterAsync(42, "local", "local", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => closed = true);
		state.RemoveConnectionAsync(42, Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource().Task);
		await service.DisconnectAsync(42).WaitAsync(TimeSpan.FromSeconds(4));
		await Assert.That(closed).IsTrue();
		await Assert.That(service.Get(42)).IsNull();
	}

	[Test]
	public async Task AuthorizationReadinessUsesTheSameBoundedDeadline()
	{
		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new(typeof(string), "subject", "not-ready", (_, _, _) => Task.CompletedTask));
		var bus = Substitute.For<IMessageBus>();
		var authorization = new SessionResumeAuthorizationService(bus, registry);
		await Assert.That(async () => await authorization.AuthorizeAsync(42, "session", new Transport(""), default)
			.WaitAsync(TimeSpan.FromSeconds(18))).Throws<OperationCanceledException>();
		await Assert.That(bus.ReceivedCalls().Any()).IsFalse();
	}

	private static ConnectionPump Pump(ConnectionServerService service, IMessageBus bus, ITerminalReplayStore replay,
		IResumeTokenStore tokens, SessionSinkRegistry sinks) => new(NullLogger<ConnectionPump>.Instance, service, bus,
		new DescriptorGeneratorService(new ConnectionServerOptions()), replay, tokens, sinks,
		new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2));

	private sealed class Transport(string first) : IDuplexTransport
	{
		private bool _first = true;
		private readonly TaskCompletionSource<string?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public List<string> Sent { get; } = [];
		public bool StallOutput { get; init; }
		public bool EndAfterHello { get; init; }
		public string Kind => "websocket";
		public string RemoteIp => "local";
		public string Hostname => "local";
		public bool IsSecure => true;
		public Task<string?> ReceiveTextAsync(CancellationToken ct)
		{
			if (!_first) return EndAfterHello ? Task.FromResult<string?>(null) : _closed.Task;
			_first = false;
			return Task.FromResult<string?>(first);
		}
		public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
		{
			var text = Encoding.UTF8.GetString(data.Span);
			if (text.Contains("resumeToken")) Ready.TrySetResult();
			else if (StallOutput) await Task.Delay(Timeout.Infinite, ct);
			Sent.Add(text);
		}
		public Task CloseAsync(CancellationToken ct = default) { _closed.TrySetResult(null); return Task.CompletedTask; }
	}
}
