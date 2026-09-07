using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Configuration;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Tests.ConnectionServer.TestSchedulers;

namespace SharpMUSH.Tests.ConnectionServer;

public class DurableSessionResumeTests
{
	[Test]
	public async Task RevocationDuringTokenConsumptionPreventsReplayAndReattachment()
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var state = Substitute.For<IConnectionStateStore>();
		var data = new ConnectionStateData
		{
			Handle = 9, PlayerObjid = "#5:1234", State = "LoggedIn", IpAddress = "old", Hostname = "old",
			ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow,
			Metadata = new() { ["SessionId"] = "session" }
		};
		state.GetConnectionAsync(9, Arg.Any<CancellationToken>()).Returns(data);
		state.TryUpdateTransportAsync(9, "session", data.PlayerObjid, data.State,
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(_ => data.Metadata.GetValueOrDefault("ResumeRevoked") != "1");
		var tokens = Substitute.For<IResumeTokenStore>();
		tokens.TryResolveAsync("token", Arg.Any<CancellationToken>()).Returns((true, 9L, "session"));
		tokens.TryConsumeAsync("token", Arg.Any<CancellationToken>()).Returns(_ =>
		{
			data.Metadata["ResumeRevoked"] = "1";
			return ValueTask.FromResult((true, 9L, "session"));
		});
		tokens.MintAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("fresh");
		var authorization = Substitute.For<ISessionResumeAuthorizationService>();
		authorization.AuthorizeAsync(9, "session", Arg.Any<IDuplexTransport>(), Arg.Any<CancellationToken>()).Returns(true);
		var replay = new TerminalReplayStore();
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, connections, bus,
			new DescriptorGeneratorService(new ConnectionServerOptions()), replay, tokens,
			new SessionSinkRegistry(), new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2),
			state, authorization);
		await pump.RestoreDormantAsync(data, DateTimeOffset.UtcNow.AddMinutes(1), default);
		await replay.AppendAsync("session", Encoding.UTF8.GetBytes("private replay"));
		var transport = new ResumeTransport("token");
		await pump.RunAsync(transport, 10, default);
		await Assert.That(transport.Sent.Any(frame => frame.Contains("reattached") || frame.Contains("private replay"))).IsFalse();
		await state.Received(1).TryUpdateTransportAsync(9, "session", data.PlayerObjid, data.State,
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
		await Assert.That(connections.Get(10)).IsNotNull();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AuthorizationOutagePreservesCredentialForNextAttempt(bool timeout)
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var tokens = new ResumeTokenService();
		var authorization = Substitute.For<ISessionResumeAuthorizationService>();
		authorization.AuthorizeAsync(9, "session", Arg.Any<IDuplexTransport>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<bool>(timeout ? new TimeoutException() : new IOException("temporary failure")));
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, connections, bus,
			new DescriptorGeneratorService(new ConnectionServerOptions()), new TerminalReplayStore(), tokens,
			new SessionSinkRegistry(), new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2),
			authorization: authorization);
		await pump.RestoreDormantAsync(new ConnectionStateData
		{
			Handle = 9, PlayerObjid = "#5:1234", State = "LoggedIn", IpAddress = "old", Hostname = "old",
			ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow,
			Metadata = new() { ["SessionId"] = "session" }
		}, DateTimeOffset.UtcNow.AddMinutes(1), default);
		var token = await tokens.MintAsync(9, "session");
		var first = new ResumeTransport(token);
		await pump.RunAsync(first, 10, default);
		await Assert.That((await tokens.TryResolveAsync(token)).Found).IsTrue();
		await Assert.That(first.Sent.Count).IsEqualTo(0);
		await Assert.That(connections.Get(10)).IsNull();
		authorization.AuthorizeAsync(9, "session", Arg.Any<IDuplexTransport>(), Arg.Any<CancellationToken>()).Returns(true);
		var retry = new ResumeTransport(token);
		await pump.RunAsync(retry, 11, default);
		await Assert.That(retry.Sent.Any(frame => frame.Contains("reattached"))).IsTrue();
		await Assert.That((await tokens.TryResolveAsync(token)).Found).IsFalse();
	}

	[Test]
	public async Task RecreatedOwnerRestoresBindingWithoutFreshRegistrationAndAuthorizesBeforeReplay()
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var descriptors = new DescriptorGeneratorService(new ConnectionServerOptions());
		var replay = new TerminalReplayStore();
		var tokens = new ResumeTokenService();
		var sinks = new SessionSinkRegistry();
		var authorization = Substitute.For<ISessionResumeAuthorizationService>();
		authorization.AuthorizeAsync(9, "session", Arg.Any<IDuplexTransport>(), Arg.Any<CancellationToken>()).Returns(true);
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, connections, bus, descriptors,
			replay, tokens, sinks, new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2),
			authorization: authorization);
		await pump.RestoreDormantAsync(new ConnectionStateData
		{
			Handle = 9, PlayerObjid = "#5:1234", State = "LoggedIn", IpAddress = "old", Hostname = "old",
			ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow,
			Metadata = new() { ["SessionId"] = "session" }
		}, DateTimeOffset.UtcNow.AddMinutes(1), default);
		await connections.Get(9)!.OutputFunction(Encoding.UTF8.GetBytes("buffered while disconnected"));
		var token = await tokens.MintAsync(9, "session");
		var transport = new ResumeTransport(token);
		await pump.RunAsync(transport, descriptors.GetNextWebSocketDescriptor(), default);
		await Assert.That(connections.Get(9)!.PlayerDbRef).IsEqualTo("#5:1234");
		await Assert.That(transport.Sent.Any(s => s.Contains("buffered while disconnected"))).IsTrue();
		await bus.DidNotReceive().Publish(Arg.Any<ConnectionEstablishedMessage>(), Arg.Any<CancellationToken>());
		await authorization.Received(1).AuthorizeAsync(9, "session", transport, Arg.Any<CancellationToken>());
		await Assert.That((await tokens.TryResolveAsync(token)).Found).IsFalse();
	}

	[Test]
	public async Task TokenForPreviousOccupantNeverAttachesToRecycledHandle()
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var descriptors = new DescriptorGeneratorService(new ConnectionServerOptions());
		var sinks = new SessionSinkRegistry();
		sinks.GetOrCreate(9).SessionId = "new-session";
		var tokens = new ResumeTokenService();
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, connections, bus, descriptors,
			new TerminalReplayStore(), tokens, sinks, new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2));
		var transport = new ResumeTransport(await tokens.MintAsync(9, "old-session"));
		await pump.RunAsync(transport, descriptors.GetNextWebSocketDescriptor(), default);
		await Assert.That(transport.Sent.Any(s => s.Contains("reattached"))).IsFalse();
		await Assert.That(sinks.Get(9)!.SessionId).IsEqualTo("new-session");
	}

	[Test]
	public async Task ForcedDisconnectClosesTransportEvenWhenRevocationFails()
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var tokens = Substitute.For<IResumeTokenStore>();
		tokens.MintAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("token");
		tokens.RevokeSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromException(new IOException("state store unavailable")));
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, connections, bus,
			Substitute.For<IDescriptorGeneratorService>(), new TerminalReplayStore(), tokens,
			new SessionSinkRegistry(), new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2));
		var transport = new HeldTransport();
		var running = pump.RunAsync(transport, 7, default);
		await transport.Registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await connections.DisconnectAsync(7);
		await running.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(transport.Closed).IsTrue();
		await Assert.That(connections.Get(7)).IsNull();
	}

	private sealed class HeldTransport : IDuplexTransport
	{
		private bool _first = true;
		private readonly TaskCompletionSource<string?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public bool Closed { get; private set; }
		public string Kind => "websocket";
		public string RemoteIp => "localhost";
		public string Hostname => "localhost";
		public bool IsSecure => true;
		public Task<string?> ReceiveTextAsync(CancellationToken ct)
		{
			if (!_first) return _closed.Task;
			_first = false;
			return Task.FromResult<string?>("{\"type\":\"hello\"}");
		}
		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
		{
			if (Encoding.UTF8.GetString(data.Span).Contains("resumeToken")) Registered.TrySetResult();
			return Task.CompletedTask;
		}
		public Task CloseAsync(CancellationToken ct = default)
		{
			Closed = true;
			_closed.TrySetResult(null);
			return Task.CompletedTask;
		}
	}

	private sealed class ResumeTransport(string token) : IDuplexTransport
	{
		private bool _first = true;
		public List<string> Sent { get; } = [];
		public string Kind => "websocket";
		public string RemoteIp => "new";
		public string Hostname => "new";
		public bool IsSecure => true;
		public Task<string?> ReceiveTextAsync(CancellationToken ct)
		{
			if (!_first) return Task.FromResult<string?>(null);
			_first = false;
			return Task.FromResult<string?>($"{{\"type\":\"resume\",\"token\":\"{token}\",\"lastSeq\":0}}");
		}
		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
		{
			Sent.Add(Encoding.UTF8.GetString(data.Span));
			return Task.CompletedTask;
		}
		public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
	}
}
