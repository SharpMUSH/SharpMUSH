using System.Net;
using System.Net.Sockets;
using System.Text;
using MarkupString;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Consumers;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The worker at a TCP address, as hosts that cannot share a volume between it and the socket server run it
/// (<c>Rendering:Address</c>).
/// </summary>
public class RenderingWorkerTcpTests
{
	private static Uri FreeAddress()
	{
		using var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		return new Uri($"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}");
	}

	[Test]
	public async Task HealthRequiresRespondingWorker()
	{
		var address = FreeAddress();
		await using var worker = SharpMUSH.RenderingWorker.Program.CreateApplication(
			[.. TestDiagnostics.HostArguments, "--Rendering:Address=" + address]);
		await Assert.That(await SharpMUSH.RenderingWorker.Program.IsHealthyAsync(new RenderAddress(address))).IsFalse();
		await worker.StartAsync();
		await Assert.That(await SharpMUSH.RenderingWorker.Program.IsHealthyAsync(new RenderAddress(address))).IsTrue();
		await worker.StopAsync();
		await Assert.That(await SharpMUSH.RenderingWorker.Program.IsHealthyAsync(new RenderAddress(address))).IsFalse();
	}

	[Test]
	public async Task MarkupRendersThroughTheAddress()
	{
		var address = FreeAddress();
		await using var worker = SharpMUSH.RenderingWorker.Program.CreateApplication(
			[.. TestDiagnostics.HostArguments, "--Rendering:Address=" + address]);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Rendering:Address"] = address.ToString()
		}).Build();
		using var renderer = new RemoteOutputRenderer(config, Substitute.For<IHostApplicationLifetime>(),
			NullLogger<RemoteOutputRenderer>.Instance);
		var output = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
		var connections = Substitute.For<IConnectionServerService>();
		connections.Get(42).Returns(new ConnectionServerService.ConnectionData(42, null,
			ConnectionServerService.ConnectionState.Connected,
			data => { output.TrySetResult(data); return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { }, null,
			new ProtocolCapabilities(Format: OutputFormat.Mxp), null, "telnet"));
		var consumer = new MarkupOutputConsumer(connections, renderer, renderer,
			NullLogger<MarkupOutputConsumer>.Instance);
		try
		{
			await worker.StartAsync(timeout.Token);
			var markup = MarkupText.Plain("over-tcp <script>");
			await consumer.HandleAsync(new MarkupOutputMessage(42, MarkupTextSerializer.Serialize(markup)), timeout.Token);
			var text = Encoding.UTF8.GetString(await output.Task.WaitAsync(timeout.Token));
			await Assert.That(text).Contains("over-tcp &lt;script&gt;");
		}
		finally
		{
			await worker.StopAsync(CancellationToken.None);
		}
	}
}
