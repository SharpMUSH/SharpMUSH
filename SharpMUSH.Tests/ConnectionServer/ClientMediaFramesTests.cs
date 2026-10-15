using System.Text;
using System.Text.Json.Nodes;
using MarkupString;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Consumers;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// sound(), music() and stopsound() reach a GMCP client that listed Client.Media as Client.Media.Play and
/// Client.Media.Stop, sent ahead of the line they stand in.
/// </summary>
public class ClientMediaFramesTests
{
	private static JsonObject Body(GmcpFrame frame) => JsonNode.Parse(frame.Json)!.AsObject();

	private static string? Field(JsonObject body, string name) =>
		body.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value?.ToString();

	private static string? Field(GmcpFrame frame, string name) => Field(Body(frame), name);

	[Test]
	public async Task AFileNameIsPlayedByName()
	{
		var frame = ClientMediaFrames.Play(new SoundMarkup("door.wav", SoundChannel.Effects, 80, 2, false));

		await Assert.That(frame).IsNotNull();
		await Assert.That(frame!.Value.Package).IsEqualTo("Client.Media.Play");
		await Assert.That(Field(frame.Value, "name")).IsEqualTo("door.wav");
		await Assert.That(Field(frame.Value, "url")).IsNull();
		await Assert.That(Field(frame.Value, "type")).IsEqualTo("sound");
		await Assert.That(Field(frame.Value, "volume")).IsEqualTo("80");
		await Assert.That(Field(frame.Value, "loops")).IsEqualTo("2");
		await Assert.That(Field(frame.Value, "key")).IsNull();
		await Assert.That(Field(frame.Value, "continue")).IsNull();
	}

	[Test]
	public async Task AnAddressIsSplitIntoWhereAndWhat()
	{
		var frame = ClientMediaFrames.Play(new SoundMarkup("https://example.com/sounds/rain.ogg?v=2", SoundChannel.Effects, null, null, false));

		await Assert.That(Field(frame!.Value, "url")).IsEqualTo("https://example.com/sounds/");
		await Assert.That(Field(frame.Value, "name")).IsEqualTo("rain.ogg?v=2");
		await Assert.That(Field(frame.Value, "volume")).IsNull();
		await Assert.That(Field(frame.Value, "loops")).IsNull();
	}

	[Test]
	public async Task AnAddressOfNoFileIsNotPlayed() =>
		await Assert.That(ClientMediaFrames.Play(new SoundMarkup("https://example.com/sounds/", SoundChannel.Effects, null, null, false))).IsNull();

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MusicIsKeyedAndSaysWhetherItContinues(bool continues)
	{
		var frame = ClientMediaFrames.Play(new SoundMarkup("theme.mid", SoundChannel.Music, null, SoundMarkup.Forever, continues));

		await Assert.That(Field(frame!.Value, "type")).IsEqualTo("music");
		await Assert.That(Field(frame.Value, "key")).IsEqualTo(ClientMediaFrames.MusicKey);
		await Assert.That(Field(frame.Value, "loops")).IsEqualTo("-1");
		await Assert.That(Field(frame.Value, "continue")).IsEqualTo(continues ? "true" : "false");
	}

	[Test]
	[Arguments(SoundChannel.Effects, "sound")]
	[Arguments(SoundChannel.Music, "music")]
	[Arguments(null, null)]
	public async Task StopNamesTheChannelOrNothing(SoundChannel? channel, string? type)
	{
		var frame = ClientMediaFrames.Stop(new SoundStopMarkup(channel));

		await Assert.That(frame.Package).IsEqualTo("Client.Media.Stop");
		await Assert.That(Field(frame, "type")).IsEqualTo(type);
	}

	[Test]
	public async Task EachSoundInTheTextIsAFrameInItsOrder()
	{
		var text = MarkupText.Concat([
			MarkupText.Sound("a.wav"), MarkupText.Sound("a.wav"), MarkupText.Plain("words"),
			MarkupText.StopSound(SoundChannel.Music), MarkupText.Music("b.mid")
		]);

		var frames = ClientMediaFrames.From(MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text)));

		await Assert.That(frames).IsNotNull();
		await Assert.That(frames!.Select(frame => frame.Package + ":" + (Field(frame, "name") ?? Field(frame, "type"))).ToArray())
			.IsEquivalentTo(["Client.Media.Play:a.wav", "Client.Media.Play:a.wav", "Client.Media.Stop:music", "Client.Media.Play:b.mid"],
				TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(ClientMediaFrames.From(MarkupText.Plain("no sound"))).IsNull();
	}

	[Test]
	[Arguments("telnet", null, false)]
	[Arguments("telnet", "", false)]
	[Arguments("telnet", "Room 1 Char 1", false)]
	[Arguments("telnet", "Client.Media 1", true)]
	[Arguments("telnet", "Client 1", true)]
	[Arguments("telnet", "client.media 1", true)]
	[Arguments("websocket", "Client.Media 1", false)]
	public async Task OnlyAClientThatListedClientMediaIsSentIt(string connectionType, string? listed, bool wanted)
	{
		await Assert.That(ClientMediaFrames.Wanted(connectionType, new ProtocolCapabilities(GmcpModules: listed))).IsEqualTo(wanted);

		var connection = new RenderContext(connectionType, new ProtocolCapabilities(GmcpModules: listed), null);
		var rendered = new MarkupOutputRenderer().Render(
			MarkupTextSerializer.Serialize(MarkupText.Concat([MarkupText.Sound("door.wav"), MarkupText.Plain("It creaks.")])), connection);

		await Assert.That(rendered.Gmcp is { Count: 1 }).IsEqualTo(wanted);
	}

	[Test]
	public async Task TheBaseAddressGetsItsClosingSlash()
	{
		await Assert.That(ClientMediaFrames.Default(null)).IsNull();
		await Assert.That(ClientMediaFrames.Default("  ")).IsNull();
		var frame = ClientMediaFrames.Default("https://example.com/media");
		await Assert.That(frame!.Value.Package).IsEqualTo("Client.Media.Default");
		await Assert.That(Field(frame.Value, "url")).IsEqualTo("https://example.com/media/");
	}

	[Test]
	public async Task TheHeaderCarriesFramesBothWays()
	{
		GmcpFrame[] frames = [new("Client.Media.Play", """{"name":"a.wav"}"""), new("Client.Media.Stop", "{}")];

		await Assert.That(ClientMediaFrames.Decode(ClientMediaFrames.Encode(frames, out var dropped))).IsEquivalentTo(frames,
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(dropped).IsEqualTo(0);
		await Assert.That(ClientMediaFrames.Decode(null)).IsNull();
		await Assert.That(ClientMediaFrames.Decode("not base64!")).IsNull();
	}

	/// <summary>
	/// The header stops at <see cref="ClientMediaFrames.MaxHeaderLength"/>, keeping the first frames in order, so it can
	/// never pass the 64 KB the socket server's HTTP client accepts.
	/// </summary>
	[Test]
	public async Task TheHeaderKeepsTheFirstFramesThatFit()
	{
		var frames = Enumerable.Range(0, 400)
			.Select(i => new GmcpFrame("Client.Media.Play", $$"""{"name":"{{i}}-{{new string('x', 200)}}.wav"}"""))
			.ToArray();

		var header = ClientMediaFrames.Encode(frames, out var dropped);
		var decoded = ClientMediaFrames.Decode(header);

		await Assert.That(header!.Length).IsLessThanOrEqualTo(ClientMediaFrames.MaxHeaderLength);
		await Assert.That(dropped).IsGreaterThan(0);
		await Assert.That(decoded!.Count).IsEqualTo(frames.Length - dropped);
		await Assert.That(decoded).IsEquivalentTo(frames.Take(decoded.Count), TUnit.Assertions.Enums.CollectionOrdering.Matching);

		GmcpFrame[] huge = [new("Client.Media.Play", $$"""{"name":"{{new string('y', 70_000)}}.wav"}""")];
		await Assert.That(ClientMediaFrames.Encode(huge, out var hugeDropped)).IsNull();
		await Assert.That(hugeDropped).IsEqualTo(1);
	}

	/// <summary>
	/// A line with more sound than fits beside it still reaches the player, through the rendering worker: the frames
	/// past the cap are left out rather than the response growing past what the socket server reads.
	/// </summary>
	[Test]
	public async Task ALineFullOfSoundsStillArrivesThroughTheRenderingWorker()
	{
		var sounds = Enumerable.Range(0, 1000).Select(i => MarkupText.Sound($"{i}-{new string('z', 300)}.wav"));
		var (written, gmcp) = await RenderThroughWorkerAsync(44,
			MarkupText.Concat([.. sounds, MarkupText.Plain("A cacophony.")]));

		await Assert.That(written).Contains("text:A cacophony.");
		await Assert.That(gmcp).IsGreaterThan(0);
		await Assert.That(gmcp).IsLessThan(1000);
	}

	/// <summary>
	/// <c>media_base_url</c> arriving or changing reaches every connection that already agreed to GMCP, once: a repeat of
	/// the same address sends nothing, and a connection without GMCP is not sent it.
	/// </summary>
	[Test]
	public async Task TheMediaDefaultReachesAgreedConnectionsWhenTheAddressChanges()
	{
		var sent = new List<(long Handle, string Package, string Url)>();
		ConnectionServerService.ConnectionData Client(long handle, string? modules, string type = "telnet") =>
			new(handle, null, ConnectionServerService.ConnectionState.Connected,
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { },
				(package, json) =>
				{
					lock (sent) sent.Add((handle, package, Field(JsonNode.Parse(json)!.AsObject(), "url")!));
					return ValueTask.CompletedTask;
				},
				new ProtocolCapabilities(GmcpModules: modules), null, type);
		var connections = Substitute.For<IConnectionServerService>();
		connections.GetAll().Returns([Client(1, ""), Client(2, "Client.Media 1"), Client(3, null), Client(4, "", "websocket")]);
		var consumer = new OutputSettingsConsumer(new OutputSettingsHolder(), connections, NullLogger<OutputSettingsConsumer>.Instance);
		var none = new Dictionary<string, string>();

		await consumer.HandleAsync(new OutputSettingsMessage(none));
		await consumer.HandleAsync(new OutputSettingsMessage(none, "https://example.com/a"));
		await consumer.HandleAsync(new OutputSettingsMessage(none, "https://example.com/a"));
		await consumer.HandleAsync(new OutputSettingsMessage(none, "https://example.com/b/"));

		await Assert.That(sent).IsEquivalentTo([
			(1L, "Client.Media.Default", "https://example.com/a/"), (2L, "Client.Media.Default", "https://example.com/a/"),
			(1L, "Client.Media.Default", "https://example.com/b/"), (2L, "Client.Media.Default", "https://example.com/b/")
		], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// Sends <paramref name="markup"/> to a telnet connection that listed Client.Media, through a real rendering worker,
	/// and returns what reached the connection in order, and how many GMCP frames did.
	/// </summary>
	private static async Task<(List<string> Written, int Gmcp)> RenderThroughWorkerAsync(long handle, MarkupText markup)
	{
		var directory = Path.Combine(Path.GetTempPath(), "sm-media-" + Guid.NewGuid().ToString("N"));
		var socketPath = Path.Combine(directory, "render.sock");
		await using var worker = SharpMUSH.RenderingWorker.Program.CreateApplication(TestDiagnostics.HostArguments, socketPath);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Rendering:SocketPath"] = socketPath
		}).Build();
		using var renderer = new RemoteOutputRenderer(config, Substitute.For<IHostApplicationLifetime>(),
			NullLogger<RemoteOutputRenderer>.Instance);
		var written = new List<string>();
		var gmcp = 0;
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var connections = Substitute.For<IConnectionServerService>();
		connections.Get(handle).Returns(new ConnectionServerService.ConnectionData(handle, null,
			ConnectionServerService.ConnectionState.Connected,
			data =>
			{
				written.Add("text:" + Encoding.UTF8.GetString(data));
				done.TrySetResult();
				return ValueTask.CompletedTask;
			},
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { },
			(package, json) =>
			{
				gmcp++;
				written.Add(package + ":" + Field(JsonNode.Parse(json)!.AsObject(), "name"));
				return ValueTask.CompletedTask;
			},
			new ProtocolCapabilities(GmcpModules: "Client.Media 1"), null, "telnet"));
		var consumer = new MarkupOutputConsumer(connections, renderer, renderer, NullLogger<MarkupOutputConsumer>.Instance);
		try
		{
			await worker.StartAsync(timeout.Token);
			await consumer.HandleAsync(new MarkupOutputMessage(handle, MarkupTextSerializer.Serialize(markup)), timeout.Token);
			await done.Task.WaitAsync(timeout.Token);
			return (written, gmcp);
		}
		finally
		{
			await worker.StopAsync(CancellationToken.None);
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>
	/// The whole path, over the rendering socket: a sound in a line reaches the connection's GMCP writer before
	/// the line's text does.
	/// </summary>
	[Test]
	public async Task FramesAreSentBeforeTheLineThroughTheRenderingWorker()
	{
		var (written, _) = await RenderThroughWorkerAsync(43,
			MarkupText.Concat([MarkupText.Sound("door.wav", 80), MarkupText.Plain("The door creaks open.")]));

		await Assert.That(written).IsEquivalentTo(["Client.Media.Play:door.wav", "text:The door creaks open."],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
