using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// A socket owner that advertises ordered prompts gets them on the output subject, which one loop
/// consumes, so a prompt and the output around it are written in publication order (#1010).
/// </summary>
public class OrderedPromptTests
{
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task OutputSubjectWritesPromptsToThePromptChannel(bool prompt)
	{
		var written = new List<string>();
		var connection = new ConnectionServerService.ConnectionData(1, null, ConnectionServerService.ConnectionState.Connected,
			_ => { written.Add("output"); return ValueTask.CompletedTask; },
			_ => { written.Add("prompt"); return ValueTask.CompletedTask; },
			() => Encoding.UTF8, () => { }, null, new ProtocolCapabilities(), null, "telnet");
		var connections = Substitute.For<IConnectionServerService>();
		connections.Get(1).Returns(connection);
		var renderer = Substitute.For<IMarkupOutputRenderer>();
		renderer.RenderAsync(Arg.Any<string>(), Arg.Any<ConnectionServerService.ConnectionData>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(new RenderedOutput("text"u8.ToArray(), false)));
		var consumer = new MarkupOutputConsumer(connections, renderer, Substitute.For<IOutputTransformService>(), NullLogger<MarkupOutputConsumer>.Instance);

		await consumer.HandleAsync(new MarkupOutputMessage(1, "text") { Prompt = prompt });

		await Assert.That(written).IsEquivalentTo([prompt ? "prompt" : "output"]);
	}

	[Test]
	public async Task SocketOwnerAdvertisesOrderedPromptsOnRegistrationAndInTheStateStore()
	{
		var bus = Substitute.For<IMessageBus>();
		var store = Substitute.For<IConnectionStateStore>();
		ConnectionStateData? persisted = null;
		store.SetConnectionAsync(46, Arg.Do<ConnectionStateData>((ConnectionStateData data) => persisted = data), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, store);

		await service.RegisterAsync(46, "1.2.3.4", "host", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { });

		await bus.Received(1).Publish(Arg.Is<ConnectionEstablishedMessage>(m => m.Handle == 46 && m.OrderedPrompts), Arg.Any<CancellationToken>());
		await Assert.That(persisted!.Metadata[ConnectionEstablishedMessage.OrderedPromptsMetadata]).IsEqualTo("1");
	}

	[Test]
	[Arguments(true, "1")]
	[Arguments(false, "0")]
	public async Task EngineRecordsWhetherTheSocketOwnerOrdersPrompts(bool advertised, string recorded)
	{
		var connections = new SharpMUSH.Library.Services.ConnectionService(Substitute.For<IPublisher>());
		var consumer = new ConnectionEstablishedConsumer(NullLogger<ConnectionEstablishedConsumer>.Instance, connections, Substitute.For<IMessageBus>());

		// An older socket owner's message has no OrderedPrompts property at all.
		var json = advertised
			? """{"Handle":7,"IpAddress":"1.2.3.4","Hostname":"host","ConnectionType":"telnet","Timestamp":"2026-09-16T00:00:00Z","OrderedPrompts":true}"""
			: """{"Handle":7,"IpAddress":"1.2.3.4","Hostname":"host","ConnectionType":"telnet","Timestamp":"2026-09-16T00:00:00Z"}""";
		await consumer.HandleAsync(System.Text.Json.JsonSerializer.Deserialize<ConnectionEstablishedMessage>(json)!);

		await Assert.That(connections.Get(7)!.Metadata[ConnectionEstablishedMessage.OrderedPromptsMetadata]).IsEqualTo(recorded);
	}
}
