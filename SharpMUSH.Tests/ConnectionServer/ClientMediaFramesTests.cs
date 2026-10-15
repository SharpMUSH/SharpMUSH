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

		await Assert.That(ClientMediaFrames.Decode(ClientMediaFrames.Encode(frames))).IsEquivalentTo(frames,
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(ClientMediaFrames.Decode(null)).IsNull();
		await Assert.That(ClientMediaFrames.Decode("not base64!")).IsNull();
	}

	/// <summary>
	/// The whole path, over the rendering socket: a sound in a line reaches the connection's GMCP writer before
	/// the line's text does.
	/// </summary>
	[Test]
	public async Task FramesAreSentBeforeTheLineThroughTheRenderingWorker()
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
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var connections = Substitute.For<IConnectionServerService>();
		connections.Get(43).Returns(new ConnectionServerService.ConnectionData(43, null,
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
				written.Add(package + ":" + Field(JsonNode.Parse(json)!.AsObject(), "name"));
				return ValueTask.CompletedTask;
			},
			new ProtocolCapabilities(GmcpModules: "Client.Media 1"), null, "telnet"));
		var consumer = new MarkupOutputConsumer(connections, renderer, renderer, NullLogger<MarkupOutputConsumer>.Instance);
		try
		{
			await worker.StartAsync(timeout.Token);
			var markup = MarkupText.Concat([MarkupText.Sound("door.wav", 80), MarkupText.Plain("The door creaks open.")]);
			await consumer.HandleAsync(new MarkupOutputMessage(43, MarkupTextSerializer.Serialize(markup)), timeout.Token);
			await done.Task.WaitAsync(timeout.Token);

			await Assert.That(written).IsEquivalentTo(["Client.Media.Play:door.wav", "text:The door creaks open."],
				TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		finally
		{
			await worker.StopAsync(CancellationToken.None);
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
	}
}
