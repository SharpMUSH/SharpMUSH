using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Consumers;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// A channel line or a page reaches a WebSocket client marked as one, so the portal can keep it out of the main
/// output; every other line goes out as before.
/// </summary>
public class CommFrameTests
{
	private const string Markup = """{"t":"psst"}""";

	private static (MarkupOutputConsumer Consumer, List<string> Written) WebSocketConsumer(bool showSeparators = false)
	{
		var written = new List<string>();
		var connection = new ConnectionServerService.ConnectionData(1, null, ConnectionServerService.ConnectionState.Connected,
			bytes => { written.Add(Encoding.UTF8.GetString(bytes)); return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => { }, null, new ProtocolCapabilities(), null, "websocket")
		{
			Preferences = new PlayerOutputPreferences(ShowSeparators: showSeparators)
		};
		var connections = Substitute.For<IConnectionServerService>();
		connections.Get(1).Returns(connection);
		var renderer = new MarkupOutputRenderer();
		return (new MarkupOutputConsumer(connections, renderer, Substitute.For<IOutputTransformService>(),
			NullLogger<MarkupOutputConsumer>.Instance), written);
	}

	[Test]
	public async Task WebSocketLinesNameTheirConversation()
	{
		var (consumer, written) = WebSocketConsumer();

		await consumer.HandleAsync(new MarkupOutputMessage(1, Markup) { Comm = CommOutput.Page });
		await consumer.HandleAsync(new MarkupOutputMessage(1, Markup) { Comm = CommOutput.Channel });
		await consumer.HandleAsync(new MarkupOutputMessage(1, Markup));

		await Assert.That(written).IsEquivalentTo([
			"""{"type":"markup","data":{"t":"psst"},"comm":"page"}""",
			"""{"type":"markup","data":{"t":"psst"},"comm":"channel"}""",
			"""{"type":"markup","data":{"t":"psst"}}"""], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>A reader who sees list separators has the line rendered by the worker, which keeps the mark.</summary>
	[Test]
	public async Task TheRenderingWorkerKeepsTheMark()
	{
		var (consumer, written) = WebSocketConsumer(showSeparators: true);

		await consumer.HandleAsync(new MarkupOutputMessage(1, MarkupString.MarkupTextSerializer.Serialize(MString.Plain("psst")))
		{
			Comm = CommOutput.Page
		});

		await Assert.That(written).Count().IsEqualTo(1);
		await Assert.That(written[0]).EndsWith(""","comm":"page"}""");
	}
}
