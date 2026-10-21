using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.ProtocolHandlers;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Tests.ConnectionServer.TestSchedulers;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>The bytes a WebSocket connection has moved, which <c>SESSION</c> shows.</summary>
public class WebSocketTrafficTests
{
	[Test]
	public async Task WebSocketConnectionCountsItsBytes()
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var traffic = new ConnectionTrafficCounters();
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, connections, bus,
			new DescriptorGeneratorService(new ConnectionServerOptions()), new TerminalReplayStore(), new ResumeTokenService(),
			new SessionSinkRegistry(), new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2),
			traffic: traffic);
		var transport = new TypingTransport("look");

		var running = pump.RunAsync(transport, 10, default);
		await transport.Typed.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await connections.Get(10)!.OutputFunction(Encoding.UTF8.GetBytes("You see nothing."));

		var counted = traffic.Snapshot().Single(connection => connection.Handle == 10);
		await Assert.That(counted.Received).IsEqualTo("look".Length);
		await Assert.That(counted.Sent).IsEqualTo(transport.OutputBytes);
		await Assert.That(counted.Sent).IsGreaterThan("You see nothing.".Length);
		await Assert.That(counted.Pending).IsEqualTo(0);

		await transport.CloseAsync();
		await running.WaitAsync(TimeSpan.FromSeconds(10));
	}

	/// <summary>Says hello, types one line, and stays open until closed.</summary>
	private sealed class TypingTransport(string line) : IDuplexTransport
	{
		private int _frame;
		private long _outputBytes;
		private readonly TaskCompletionSource<string?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Typed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		/// <summary>The bytes of every frame but the resume token, which is the session's own bookkeeping.</summary>
		public long OutputBytes => Interlocked.Read(ref _outputBytes);
		public string Kind => "websocket";
		public string RemoteIp => "localhost";
		public string Hostname => "localhost";
		public bool IsSecure => false;

		public Task<string?> ReceiveTextAsync(CancellationToken ct)
		{
			switch (_frame++)
			{
				case 0:
					return Task.FromResult<string?>("{\"type\":\"hello\"}");
				case 1:
					return Task.FromResult<string?>(line);
				default:
					Typed.TrySetResult();
					return _closed.Task;
			}
		}

		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
		{
			if (!Encoding.UTF8.GetString(data.Span).Contains("resumeToken"))
				Interlocked.Add(ref _outputBytes, data.Length);
			return Task.CompletedTask;
		}

		public Task CloseAsync(CancellationToken ct = default)
		{
			_closed.TrySetResult(null);
			return Task.CompletedTask;
		}
	}
}
