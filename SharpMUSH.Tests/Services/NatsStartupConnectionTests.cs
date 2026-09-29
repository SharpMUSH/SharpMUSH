using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A host's first NATS connect must survive a failed attempt: one refused or slow connect used to fail the
/// singleton, and every test sharing that host failed with the cached error.
/// </summary>
public class NatsStartupConnectionTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	[Test]
	public async Task MessageBusConnectsAfterTheFirstAttemptFails()
	{
		using var proxy = new DropFirstConnectionProxy(NatsTestServer.Instance.GetMappedPublicPort(4222));
		var id = Guid.NewGuid().ToString("N");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(
			new NatsOptions { Url = proxy.Url, StreamName = "STARTUP" + id, SubjectPrefix = "startup" + id },
			NullLogger<NatsJetStreamMessageBus>.Instance, timeout.Token);
		await bus.Publish(new StartupMessage("test"), timeout.Token);
		await Assert.That(proxy.Accepted).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task UnreachableBrokerFailsAtTheConnectDeadline()
	{
		using var proxy = new DropFirstConnectionProxy(upstreamPort: null);
		var stopwatch = Stopwatch.StartNew();
		await Assert.That(async () => await NatsJetStreamMessageBus.CreateAsync(
				new NatsOptions { Url = proxy.Url, ConnectTimeout = TimeSpan.FromSeconds(1) },
				NullLogger<NatsJetStreamMessageBus>.Instance))
			.Throws<TimeoutException>();
		await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(15));
	}

	public record StartupMessage(string Value);

	/// <summary>
	/// Closes the first connection it accepts before the broker says anything, then forwards later
	/// connections to <c>upstreamPort</c>. With no upstream it closes every connection.
	/// </summary>
	private sealed class DropFirstConnectionProxy : IDisposable
	{
		private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
		private readonly CancellationTokenSource _stop = new();
		private readonly int? _upstreamPort;
		private int _accepted;

		public DropFirstConnectionProxy(int? upstreamPort)
		{
			_upstreamPort = upstreamPort;
			_listener.Start();
			_ = AcceptLoopAsync();
		}

		public string Url => $"nats://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
		public int Accepted => Volatile.Read(ref _accepted);

		private async Task AcceptLoopAsync()
		{
			try
			{
				while (true)
				{
					var client = await _listener.AcceptTcpClientAsync(_stop.Token);
					if (Interlocked.Increment(ref _accepted) == 1 || _upstreamPort is not { } port)
					{
						client.Dispose();
						continue;
					}
					_ = ForwardAsync(client, port);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private async Task ForwardAsync(TcpClient client, int port)
		{
			using (client)
			using (var upstream = new TcpClient())
			{
				try
				{
					await upstream.ConnectAsync(IPAddress.Loopback, port, _stop.Token);
					var down = client.GetStream();
					var up = upstream.GetStream();
					await Task.WhenAny(down.CopyToAsync(up, _stop.Token), up.CopyToAsync(down, _stop.Token));
				}
				catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
				{
				}
			}
		}

		public void Dispose()
		{
			_stop.Cancel();
			_listener.Stop();
			_stop.Dispose();
		}
	}
}
