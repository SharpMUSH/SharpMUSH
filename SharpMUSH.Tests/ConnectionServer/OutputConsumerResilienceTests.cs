using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

public class OutputConsumerResilienceTests
{
	private static ConnectionServerService.ConnectionData Connection(long handle, Func<byte[], ValueTask> output,
		ProtocolCapabilities? capabilities = null, PlayerOutputPreferences? preferences = null) => new(handle, null,
		ConnectionServerService.ConnectionState.Connected, output, output, () => Encoding.UTF8, () => { }, null,
		capabilities ?? new ProtocolCapabilities(), preferences, "telnet");

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	public async Task GuidedPromptRejectsAReusedTransportHandle(bool replacedBeforeReceive, bool replacedDuringRender)
	{
		var writes = 0;
		var connections = Substitute.For<IConnectionServerService>();
		var current = Connection(1, _ => { writes++; return ValueTask.CompletedTask; }) with { SessionId = replacedBeforeReceive ? "replacement" : "original" };
		connections.Get(1).Returns(_ => current);
		var renderer = Substitute.For<IMarkupOutputRenderer>();
		renderer.RenderAsync(Arg.Any<string>(), Arg.Any<ConnectionServerService.ConnectionData>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			if (replacedDuringRender) current = current with { SessionId = "replacement" };
			return ValueTask.FromResult(new RenderedOutput("prompt"u8.ToArray(), false));
		});
		var message = System.Text.Json.JsonSerializer.Deserialize<MarkupPromptMessage>("""{"Handle":1,"Markup":"private prompt","SessionId":"original"}""")!;
		await new MarkupPromptConsumer(connections, renderer, Substitute.For<IOutputTransformService>(), NullLogger<MarkupPromptConsumer>.Instance).HandleAsync(message);
		await Assert.That(writes).IsEqualTo(replacedBeforeReceive || replacedDuringRender ? 0 : 1);
	}

	[Test]
	public async Task BroadcastRendersOncePerValueEqualCapabilitiesAndPreferences()
	{
		var received = new Dictionary<long, byte[]>();
		ConnectionServerService.ConnectionData Client(long handle, ProtocolCapabilities caps, PlayerOutputPreferences prefs) =>
			Connection(handle, bytes => { received[handle] = bytes; return ValueTask.CompletedTask; }, caps, prefs);
		var clients = new[]
		{
			Client(1, new(), new()), Client(2, new(), new()),
			Client(3, new(SupportsAnsi: false), new()), Client(4, new(), new(ColorEnabled: false))
		};
		var connections = Substitute.For<IConnectionServerService>();
		connections.GetAll().Returns(clients);
		var transform = Substitute.For<IOutputTransformService>();
		transform.TransformAsync(Arg.Any<byte[]>(), Arg.Any<ProtocolCapabilities>(), Arg.Any<PlayerOutputPreferences?>(), Arg.Any<CancellationToken>())
			.Returns(call => ValueTask.FromResult(new[]
			{
				(byte)(call.Arg<ProtocolCapabilities>().SupportsAnsi ? 1 : 0),
				(byte)(call.Arg<PlayerOutputPreferences>().ColorEnabled ? 1 : 0)
			}));
		await new BroadcastConsumer(connections, transform, NullLogger<BroadcastConsumer>.Instance)
			.HandleAsync(new BroadcastMessage("broadcast"u8.ToArray()));
		await Assert.That(transform.ReceivedCalls().Count()).IsEqualTo(3);
		await Assert.That(received.Count).IsEqualTo(4);
		await Assert.That(ReferenceEquals(received[1], received[2])).IsTrue();
		await Assert.That(received[3][0]).IsEqualTo((byte)0);
		await Assert.That(received[4][1]).IsEqualTo((byte)0);
	}

	[Test]
	[Arguments("telnet")]
	[Arguments("prompt")]
	[Arguments("markup")]
	[Arguments("markup-prompt")]
	[Arguments("broadcast")]
	public async Task ConsumerPropagatesCancellationIntoTransformation(string kind)
	{
		using var caller = new CancellationTokenSource();
		var connections = Substitute.For<IConnectionServerService>();
		var writes = 0;
		var client = Connection(1, _ => { writes++; return ValueTask.CompletedTask; });
		connections.Get(1).Returns(client);
		connections.GetAll().Returns([client]);
		var transform = Substitute.For<IOutputTransformService>();
		transform.TransformAsync(Arg.Any<byte[]>(), Arg.Any<ProtocolCapabilities>(), Arg.Any<PlayerOutputPreferences?>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				caller.Cancel();
				return new ValueTask<byte[]>(Task.FromCanceled<byte[]>(call.Arg<CancellationToken>()));
			});
		var renderer = Substitute.For<IMarkupOutputRenderer>();
		renderer.RenderAsync(Arg.Any<string>(), client, caller.Token)
			.Returns(new RenderedOutput("rendered"u8.ToArray(), true));
		Task Handle() => kind switch
		{
			"telnet" => new TelnetOutputConsumer(connections, transform, NullLogger<TelnetOutputConsumer>.Instance)
				.HandleAsync(new TelnetOutputMessage(1, "data"u8.ToArray()), caller.Token),
			"prompt" => new TelnetPromptConsumer(connections, transform, NullLogger<TelnetPromptConsumer>.Instance)
				.HandleAsync(new TelnetPromptMessage(1, "data"u8.ToArray()), caller.Token),
			"markup" => new MarkupOutputConsumer(connections, renderer, transform, NullLogger<MarkupOutputConsumer>.Instance)
				.HandleAsync(new MarkupOutputMessage(1, "data"), caller.Token),
			"markup-prompt" => new MarkupPromptConsumer(connections, renderer, transform, NullLogger<MarkupPromptConsumer>.Instance)
				.HandleAsync(new MarkupPromptMessage(1, "data"), caller.Token),
			_ => new BroadcastConsumer(connections, transform, NullLogger<BroadcastConsumer>.Instance)
				.HandleAsync(new BroadcastMessage("data"u8.ToArray()), caller.Token)
		};
		await Assert.That(async () => await Handle().WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();
		await Assert.That(writes).IsEqualTo(0);
	}

	[Test]
	public async Task NullWebSocketMessagesDoNotWriteOutput()
	{
		var connections = Substitute.For<IConnectionServerService>();
		var writes = 0;
		connections.Get(1).Returns(Connection(1, _ => { writes++; return ValueTask.CompletedTask; }));
		await new WebSocketOutputConsumer(connections, NullLogger<WebSocketOutputConsumer>.Instance)
			.HandleAsync(new WebSocketOutputMessage(1, null!));
		await new WebSocketPromptConsumer(connections, NullLogger<WebSocketPromptConsumer>.Instance)
			.HandleAsync(new WebSocketPromptMessage(1, null!));
		await Assert.That(writes).IsEqualTo(0);
		await Assert.That(connections.ReceivedCalls().Count()).IsEqualTo(0);
	}
}
