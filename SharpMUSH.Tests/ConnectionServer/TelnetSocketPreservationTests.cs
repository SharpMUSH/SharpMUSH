using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.ConnectionServer;

public class TelnetSocketPreservationTests
{
	// A separate broker prevents another socket owner's competing durable consumer from taking our output.
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerClass)]
	public required NatsTestServer NatsTestServer { get; init; }

	[Test]
	public async Task SameTelnetTcpSocketReceivesQueuedOutputAfterRenderingWorkerReplacement()
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var ct = timeout.Token;
		var directory = Path.Combine(Path.GetTempPath(), "sm-tcp-" + Guid.NewGuid().ToString("N"));
		var socketPath = Path.Combine(directory, "render.sock");
		var natsUrl = $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}";
		var retry = new RetryLogger();
		WebApplication? worker = SharpMUSH.RenderingWorker.Program.CreateApplication([], socketPath);
		await using var owner = await SharpMUSH.ConnectionServer.Program.CreateHostBuilderAsync(
			["--ConnectionServer:TelnetPort=0", "--ConnectionServer:HttpPort=0", "--ConnectionServer:TelnetSslPort=0",
				"--Rendering:SocketPath=" + socketPath], natsUrl,
			services => services.AddSingleton<ILogger<RemoteOutputRenderer>>(retry));
		using var client = new TcpClient();
		try
		{
			await worker.StartAsync(ct);
			await owner.StartAsync(ct);
			await owner.Services.GetRequiredService<NatsConsumerRegistry>().WaitUntilReadyAsync(ct);
			// Kestrel publishes addresses in listener-registration order: telnet first, HTTP second.
			var telnetPort = new Uri(owner.Urls.First()).Port;
			await client.ConnectAsync(IPAddress.Loopback, telnetPort, ct);
			var stream = client.GetStream();
			var originalEndpoint = client.Client.LocalEndPoint;
			var connections = owner.Services.GetRequiredService<IConnectionServerService>();
			while (!connections.GetAll().Any()) await Task.Delay(25, ct);
			var connection = connections.GetAll().Single();
			await using var engineBus = await NatsJetStreamMessageBus.CreateAsync(new NatsOptions
			{
				Url = natsUrl, StreamName = "SHARPMUSH-MS", SubjectPrefix = "sharpmush.ms"
			}, NullLogger<NatsJetStreamMessageBus>.Instance, ct);

			await engineBus.Publish(new TelnetOutputMessage(connection.Handle, "before-worker-update\r\n"u8.ToArray()), ct);
			await ReadThroughAsync(stream, "before-worker-update", ct);
			await worker.StopAsync(ct);
			await worker.DisposeAsync();
			worker = null;

			await engineBus.Publish(new TelnetOutputMessage(connection.Handle, "after-worker-update\r\n"u8.ToArray()), ct);
			await retry.Unavailable.Task.WaitAsync(ct);
			var pendingRead = ReadThroughAsync(stream, "after-worker-update", ct);
			await Assert.That(pendingRead.IsCompleted).IsFalse();
			await Assert.That(ReferenceEquals(connections.Get(connection.Handle), connection)).IsTrue();

			worker = SharpMUSH.RenderingWorker.Program.CreateApplication([], socketPath);
			await worker.StartAsync(ct);
			await pendingRead;
			await Assert.That(client.Client.LocalEndPoint).IsEqualTo(originalEndpoint);
			await Assert.That(ReferenceEquals(connections.Get(connection.Handle), connection)).IsTrue();
			await Assert.That(connections.GetAll().Count()).IsEqualTo(1);
		}
		finally
		{
			client.Close();
			using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			await owner.StopAsync(shutdown.Token);
			if (worker is not null)
			{
				await worker.StopAsync(shutdown.Token);
				await worker.DisposeAsync();
			}
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task ReadThroughAsync(NetworkStream stream, string marker, CancellationToken ct)
	{
		var text = new StringBuilder();
		var buffer = new byte[4096];
		while (!text.ToString().Contains(marker, StringComparison.Ordinal))
		{
			var length = await stream.ReadAsync(buffer, ct);
			if (length == 0) throw new IOException("Telnet TCP connection closed while waiting for output.");
			text.Append(Encoding.ASCII.GetString(buffer, 0, length));
			if (text.Length > 65536) throw new IOException("Expected output marker did not arrive within 64 KiB.");
		}
	}

	private sealed class RetryLogger : ILogger<RemoteOutputRenderer>
	{
		public TaskCompletionSource Unavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			if (logLevel == LogLevel.Warning) Unavailable.TrySetResult();
		}
	}
}
