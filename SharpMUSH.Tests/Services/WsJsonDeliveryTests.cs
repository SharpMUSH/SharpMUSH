using System.Collections.Concurrent;
using System.Text;
using NSubstitute;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>wsjson()</c> text is split per connection as it leaves: a WebSocket connection gets the line without the
/// <c>&lt;default&gt;</c> and the JSON as a <c>{"type":"json"}</c> frame, every other connection the line with the
/// <c>&lt;default&gt;</c> and no JSON. A line that was only data for the other kind of reader is not sent at all.
/// </summary>
public class WsJsonDeliveryTests
{
	private static readonly DBRef Player = new(43, null);
	private const long WebSocket = 8;
	private const long Telnet = 9;

	private static (NotifyService Service, IMessageBus Bus) BuildService()
	{
		var connections = Substitute.For<IConnectionService>();
		var portal = Connection(WebSocket, "websocket");
		var telnet = Connection(Telnet, "telnet");
		connections.Get(WebSocket).Returns(portal);
		connections.Get(Telnet).Returns(telnet);
		connections.Get(Player).Returns(_ => new[] { portal, telnet }.ToAsyncEnumerable());
		var bus = Substitute.For<IMessageBus>();
		return (new NotifyService(bus, connections, Substitute.For<ILocalizationService>(), DisabledRealityPolicy.Instance,
			null, null, null), bus);
	}

	private static IConnectionService.ConnectionData Connection(long handle, string type) =>
		new(handle, Player, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string> { ["ConnectionType"] = type }));

	private static object[] Published(IMessageBus bus) =>
		[.. bus.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<IHandleMessage>().Cast<object>()];

	private static string[] Lines(IMessageBus bus, long handle) =>
		[.. Published(bus).OfType<MarkupOutputMessage>().Where(m => m.Handle == handle)
			.Select(m => MarkupTextSerializer.Deserialize(m.Markup).ToPlainText())];

	private static string[] Objects(IMessageBus bus, long handle) =>
		[.. Published(bus).OfType<WebSocketOutputMessage>().Where(m => m.Handle == handle).Select(m => m.Data)];

	[Test]
	public async Task EachConnectionGetsItsOwnReading()
	{
		var (service, bus) = BuildService();
		var line = MString.Concat([MString.Plain("Sheet: "), WebSocketData.Build("{\"hp\":7}", MString.Plain("HP 7"))]);

		await service.Notify(Player, line, sender: null);

		await Assert.That(Lines(bus, WebSocket)).IsEquivalentTo(["Sheet: "]);
		await Assert.That(Objects(bus, WebSocket)).IsEquivalentTo([WebSocketOobEnvelope.BuildJson("{\"hp\":7}")]);
		await Assert.That(Lines(bus, Telnet)).IsEquivalentTo(["Sheet: HP 7"]);
		await Assert.That(Objects(bus, Telnet)).IsEmpty();
	}

	[Test]
	public async Task TheJsonGoesOutAsAJsonFrame()
	{
		var (service, bus) = BuildService();

		await service.Notify(Player, WebSocketData.Build("{\"hp\":7}", MString.Empty), sender: null);

		await Assert.That(Objects(bus, WebSocket)).IsEquivalentTo(["{\"type\":\"json\",\"data\":{\"hp\":7}}"]);
	}

	[Test]
	public async Task ALineThatWasOnlyDataIsNotSent()
	{
		var (service, bus) = BuildService();

		await service.Notify(Player, WebSocketData.Build("[1,2]", MString.Empty), sender: null);

		await Assert.That(Lines(bus, WebSocket)).IsEmpty();
		await Assert.That(Objects(bus, WebSocket)).Count().IsEqualTo(1);
		await Assert.That(Lines(bus, Telnet)).IsEmpty()
			.Because("PennMUSH's wsjson() with no <default> is nothing to a reader without a WebSocket");
	}

	[Test]
	public async Task TextWithoutWsjsonIsSentAsItIs()
	{
		var (service, bus) = BuildService();

		await service.Notify(Player, "You see a quay.", sender: null);

		await Assert.That(Lines(bus, WebSocket)).IsEquivalentTo(["You see a quay."]);
		await Assert.That(Lines(bus, Telnet)).IsEquivalentTo(["You see a quay."]);
		await Assert.That(Objects(bus, WebSocket)).IsEmpty();
	}
}
