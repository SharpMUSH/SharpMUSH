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
		await using var proxy = new DropFirstConnectionProxy(NatsTestServer.Instance.GetMappedPublicPort(4222));
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
		await using var proxy = new DropFirstConnectionProxy(upstreamPort: null);
		var stopwatch = Stopwatch.StartNew();
		await Assert.That(async () => await NatsJetStreamMessageBus.CreateAsync(
				new NatsOptions { Url = proxy.Url, ConnectTimeout = TimeSpan.FromSeconds(1) },
				NullLogger<NatsJetStreamMessageBus>.Instance))
			.Throws<TimeoutException>();
		await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(15));
	}

	[Test]
	public async Task ProxyDisposalDrainsBothDirectionsOfAnOpenConnection()
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using var upstream = new TcpListener(IPAddress.Loopback, 0);
		upstream.Start();
		await using var proxy = new DropFirstConnectionProxy(((IPEndPoint)upstream.LocalEndpoint).Port);
		var endpoint = new Uri(proxy.Url);
		using var first = new TcpClient();
		await first.ConnectAsync(IPAddress.Loopback, endpoint.Port, timeout.Token);
		var buffer = new byte[1];
		await Assert.That(await first.GetStream().ReadAsync(buffer, timeout.Token)).IsEqualTo(0);
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, endpoint.Port, timeout.Token);
		using var server = await upstream.AcceptTcpClientAsync(timeout.Token);
		await client.GetStream().WriteAsync(new byte[] { 42 }, timeout.Token);
		await server.GetStream().ReadExactlyAsync(buffer, timeout.Token);
		await Assert.That(buffer[0]).IsEqualTo((byte)42);
		await server.GetStream().WriteAsync(new byte[] { 24 }, timeout.Token);
		await client.GetStream().ReadExactlyAsync(buffer, timeout.Token);
		await Assert.That(buffer[0]).IsEqualTo((byte)24);

		await proxy.DisposeAsync().AsTask().WaitAsync(timeout.Token);
		await Assert.That(proxy.HasPendingWork).IsFalse();
		await Assert.That(await client.GetStream().ReadAsync(buffer, timeout.Token)).IsEqualTo(0);
		await Assert.That(await server.GetStream().ReadAsync(buffer, timeout.Token)).IsEqualTo(0);
	}

	public record StartupMessage(string Value);

	/// <summary>
	/// Closes the first connection it accepts before the broker says anything, then forwards later
	/// connections to <c>upstreamPort</c>. With no upstream it closes every connection.
	/// </summary>
	private sealed class DropFirstConnectionProxy : IAsyncDisposable
	{
		private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
		private readonly CancellationTokenSource _stop = new();
		private readonly int? _upstreamPort;
		private int _accepted;
		private readonly List<Task> _forwarders = [];
		private readonly Task _acceptLoop;
		private Task? _disposal;

		public DropFirstConnectionProxy(int? upstreamPort)
		{
			_upstreamPort = upstreamPort;
			_listener.Start();
			_acceptLoop = AcceptLoopAsync();
		}

		public string Url => $"nats://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
		public int Accepted => Volatile.Read(ref _accepted);
		// Read only after disposal has joined the accept loop, which owns this list.
		public bool HasPendingWork => !_acceptLoop.IsCompleted || _forwarders.Any(task => !task.IsCompleted);

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
					_forwarders.Add(ForwardAsync(client, port));
				}
			}
			catch (Exception ex) when (_stop.IsCancellationRequested &&
				(ex is OperationCanceledException or SocketException or ObjectDisposedException))
			{
			}
		}

		private async Task ForwardAsync(TcpClient client, int port)
		{
			using (client)
			using (var upstream = new TcpClient())
			using (var forwarding = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
			{
				try
				{
					await upstream.ConnectAsync(IPAddress.Loopback, port, forwarding.Token);
				}
				catch (Exception ex) when (_stop.IsCancellationRequested &&
					(ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException))
				{
					return;
				}
				var down = client.GetStream();
				var up = upstream.GetStream();
				var toUpstream = CopyAsync(down, up, forwarding.Token);
				var toClient = CopyAsync(up, down, forwarding.Token);
				try
				{
					await Task.WhenAny(toUpstream, toClient);
				}
				finally
				{
					// One closed direction ends the connection. Stop its sibling before joining
					// both tasks; awaiting WhenAny alone would leave a copy and faults unowned.
					forwarding.Cancel();
					client.Dispose();
					upstream.Dispose();
					try
					{
						await Task.WhenAll(toUpstream, toClient);
					}
					catch (Exception) when (IsExpectedClose(toUpstream) && IsExpectedClose(toClient))
					{
					}
				}
			}
		}

		// Capture synchronous stream setup errors in the owned tasks as well.
		private static async Task CopyAsync(Stream source, Stream destination, CancellationToken ct) =>
			await source.CopyToAsync(destination, ct);

		private static bool IsExpectedClose(Task task) => task.Exception is not { } error ||
			error.InnerExceptions.All(ex => ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException);

		public ValueTask DisposeAsync() => new(_disposal ??= StopAsync());

		private async Task StopAsync()
		{
			try
			{
				_stop.Cancel();
				_listener.Stop();
				try
				{
					await _acceptLoop;
				}
				finally
				{
					// Only the accept loop registers forwarders, and it has now stopped.
					await Task.WhenAll(_forwarders);
				}
			}
			finally
			{
				_stop.Dispose();
			}
		}
	}
}
