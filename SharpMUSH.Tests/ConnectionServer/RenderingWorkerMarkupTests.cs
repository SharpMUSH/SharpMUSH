using System.Text;
using MarkupString;
using MarkupString.Html;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

public class RenderingWorkerMarkupTests
{
	[Test]
	[Arguments(OutputFormat.Pueblo)]
	[Arguments(OutputFormat.Mxp)]
	public async Task MarkupMessageRendersLinksAndEscapesPlainTextThroughUnixSocket(OutputFormat format)
	{
		var directory = Path.Combine(Path.GetTempPath(), "sm-markup-" + Guid.NewGuid().ToString("N"));
		var socketPath = Path.Combine(directory, "render.sock");
		await using var worker = SharpMUSH.RenderingWorker.Program.CreateApplication([], socketPath);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Rendering:SocketPath"] = socketPath
		}).Build();
		using var renderer = new RemoteOutputRenderer(config, lifetime, NullLogger<RemoteOutputRenderer>.Instance);
		var output = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
		var connections = Substitute.For<IConnectionServerService>();
		connections.Get(42).Returns(new ConnectionServerService.ConnectionData(42, null,
			ConnectionServerService.ConnectionState.Connected,
			data => { output.TrySetResult(data); return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { }, null,
			new ProtocolCapabilities(Format: format), null, "telnet"));
		var consumer = new MarkupOutputConsumer(connections, renderer, renderer,
			NullLogger<MarkupOutputConsumer>.Instance);
		try
		{
			await worker.StartAsync(timeout.Token);
			var link = MarkupText.Wrap(HtmlMarkup.Create("send", "href=\"look\""), MarkupText.Plain("known-link"));
			var markup = MarkupText.Concat(link, MarkupText.Plain(" <script>&\nsecond-line"));
			await consumer.HandleAsync(new MarkupOutputMessage(42, MarkupTextSerializer.Serialize(markup)), timeout.Token);
			var text = Encoding.UTF8.GetString(await output.Task.WaitAsync(timeout.Token));
			await Assert.That(text.ToUpperInvariant()).Contains("<SEND");
			await Assert.That(text.ToUpperInvariant()).Contains("HREF=\"LOOK\"");
			await Assert.That(text).Contains("known-link");
			await Assert.That(text).Contains("&lt;script&gt;&amp;");
			await Assert.That(text).Contains("\r\n");
			await Assert.That(text.Contains("\x1b[1z", StringComparison.Ordinal)).IsEqualTo(format == OutputFormat.Mxp);
		}
		finally
		{
			await worker.StopAsync(CancellationToken.None);
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
	}
}
