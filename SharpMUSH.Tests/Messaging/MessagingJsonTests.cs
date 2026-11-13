using System.Buffers;
using System.Text;
using NATS.Client.Serializers.Json;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Messaging;

/// <summary>
/// The socket server and the rendering worker are compiled ahead of time, where only
/// <see cref="MessagingJsonContext"/>'s generated contracts exist. Every bus message needs one, and it must
/// write what the reflection-based serializer the bus used before writes, so either side reads the other.
/// </summary>
public class MessagingJsonTests
{
	public static IEnumerable<Type> MessageTypes() =>
		typeof(IHandleMessage).Assembly.GetTypes()
			.Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true }
				&& type.Namespace == typeof(IHandleMessage).Namespace
				&& type.Name.EndsWith("Message", StringComparison.Ordinal));

	[Test]
	[MethodDataSource(nameof(MessageTypes))]
	public async Task EveryMessageHasAGeneratedContract(Type type) =>
		await Assert.That(MessagingJsonContext.Default.GetTypeInfo(type)).IsNotNull();

	[Test]
	public async Task ConnectionStateHasAGeneratedContract() =>
		await Assert.That(MessagingJsonContext.Default.GetTypeInfo(typeof(ConnectionStateData))).IsNotNull();

	[Test]
	public async Task WritesWhatReflectionWrites()
	{
		await AssertSameJson(new ConnectionEstablishedMessage(4, "203.0.113.9", "host.example", "telnet",
			DateTimeOffset.UnixEpoch, IsSecure: true, SessionId: "s1")
		{ OrderedPrompts = true, SignIn = true, InitialGraphics = "sixel" });
		await AssertSameJson(new TerminalReportMessage(4, new TerminalProbeResult(true, null, "kitty 0.30", 8, 16)));
		await AssertSameJson(new UpdateTerminalFeaturesMessage(4, new TerminalPins(Hyperlinks: true, Graphics: "kitty", StripAccents: true)));
		await AssertSameJson(new OutputSettingsMessage(new Dictionary<string, string> { ["é"] = "e" }, "https://example.org/", 30, true));
		await AssertSameJson(new MSSPReportMessage([new MSSPVariable("NAME", ["Game"]), new MSSPVariable("PORT", ["4201", "4202"])]));
		await AssertSameJson(new TelnetOutputMessage(4, [0xff, 0xfb, 0x01, (byte)'x']));
		await AssertSameJson(new MarkupOutputMessage(4, """{"t":"hi <b>é</b>"}""") { SessionId = "s1", Prompt = true, ClearPrompt = true });
		await AssertSameJson(new ConnectionTrafficReportMessage(Guid.Empty, [new ConnectionTraffic(4, 10, 20, 0)]));
	}

	[Test]
	public async Task ConnectionStateWritesWhatReflectionWrote()
	{
		var state = new ConnectionStateData
		{
			Handle = 4, PlayerObjid = "#5:1", State = "LoggedIn", IpAddress = "203.0.113.9", Hostname = "host.example",
			ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UnixEpoch, LastSeen = DateTimeOffset.UnixEpoch,
			Metadata = { ["SessionId"] = "s1" }
		};

		await Assert.That(System.Text.Json.JsonSerializer.Serialize(state, MessagingJson.TypeInfo<ConnectionStateData>()))
			.IsEqualTo(System.Text.Json.JsonSerializer.Serialize(state));
	}

	[Test]
	public async Task ProbeResultRoundTrips()
	{
		var message = new TerminalReportMessage(4, new TerminalProbeResult(true, null, "kitty", 8, 16));

		var restored = Read<TerminalReportMessage>(Write(MessagingNatsSerializer<TerminalReportMessage>.Default, message));

		await Assert.That(restored).IsEqualTo(message);
	}

	private static async Task AssertSameJson<T>(T message) where T : class
	{
		var generated = Encoding.UTF8.GetString(Write(MessagingNatsSerializer<T>.Default, message));
		var reflection = Encoding.UTF8.GetString(Write(NatsJsonSerializer<T>.Default, message));

		await Assert.That(generated).IsEqualTo(reflection);
		await Assert.That(Read<T>(Encoding.UTF8.GetBytes(reflection))).IsNotNull();
	}

	private static byte[] Write<T>(NATS.Client.Core.INatsSerializer<T> serializer, T value)
	{
		var buffer = new ArrayBufferWriter<byte>();
		serializer.Serialize(buffer, value);
		return buffer.WrittenSpan.ToArray();
	}

	private static T? Read<T>(byte[] bytes) =>
		MessagingNatsSerializer<T>.Default.Deserialize(new ReadOnlySequence<byte>(bytes));
}
