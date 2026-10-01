using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// A reload resumes the terminal's game session from the point it kept in sessionStorage
/// (<see cref="TerminalResumeStore"/>), against a websocket server that answers the first frame the
/// way <c>ConnectionPump</c> does: <c>reattached</c>, the frames after <c>lastSeq</c>, then a new
/// token when it rebinds; a fresh session's token and frames, numbered from 1, when it refuses.
/// </summary>
public class WebSocketClientResumeTests
{
	private static readonly TerminalIdentity Alice = new("alice", "#5:100");
	private static readonly string AliceKey = TerminalResumeStore.KeyFor("play", Alice);

	private static string Seq(long seq, string data) => JsonSerializer.Serialize(new { type = "seq", seq, data });
	private static string Token(string token) => JsonSerializer.Serialize(new { type = "resumeToken", token });
	private const string Reattached = "{\"type\":\"reattached\"}";
	private const string Bye = "{\"type\":\"bye\"}";

	private static (WebSocketClientService Client, ConcurrentQueue<string> Surfaced) NewClient(FakeResumeJs js)
	{
		var client = new WebSocketClientService(NullLogger<WebSocketClientService>.Instance, new TerminalResumeStore(js));
		var surfaced = new ConcurrentQueue<string>();
		client.MessageReceived += (_, message) => surfaced.Enqueue(message);
		return (client, surfaced);
	}

	private static string Point(string token, long lastSeq) =>
		$"{{\"token\":\"{token}\",\"lastSeq\":{lastSeq}}}";

	private static bool IsResume(string frame) => frame.Contains("\"type\":\"resume\"");

	[Test]
	public async Task A_reload_resumes_from_the_stored_point_and_drops_frames_already_shown()
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("tok-1", 5));
		await using var server = await ScriptedTerminalServer.StartAsync(first => IsResume(first)
			? [Reattached, Seq(4, "four"), Seq(5, "five"), Seq(6, "six"), Token("tok-2")]
			: [Token("fresh")]);
		var (client, surfaced) = NewClient(js);
		var reattached = 0;
		client.Reattached += (_, _) => reattached++;

		await client.ConnectAsync(server.Uri, Alice);

		var first = await server.FirstFrameAsync();
		using var firstFrame = JsonDocument.Parse(first);
		await Assert.That(firstFrame.RootElement.GetProperty("type").GetString()).IsEqualTo("resume");
		await Assert.That(firstFrame.RootElement.GetProperty("token").GetString()).IsEqualTo("tok-1");
		await Assert.That(firstFrame.RootElement.GetProperty("lastSeq").GetInt64()).IsEqualTo(5L);
		await Assert.That(client.Resumed).IsTrue();
		await Assert.That(reattached).IsEqualTo(1);

		await Assert.That(await Eventually.TrueAsync(() => js.StoredValue(AliceKey) == Point("tok-2", 6))).IsTrue()
			.Because($"the rotated token is kept at once, got {js.StoredValue(AliceKey)}");
		await Assert.That(surfaced.ToArray()).IsEquivalentTo(["six"]);

		await client.DisposeAsync();
	}

	/// <summary>
	/// An expired token: the server registers a fresh session instead. Its frames count from 1, so the
	/// stale <c>lastSeq</c> must not swallow them, and the refused token is gone from storage.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task A_refused_resume_starts_fresh_and_replaces_the_stored_point(bool frameBeforeToken)
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("expired", 40));
		await using var server = await ScriptedTerminalServer.StartAsync(_ => frameBeforeToken
			? [Seq(1, "banner"), Token("tok-new")]
			: [Token("tok-new"), Seq(1, "banner")]);
		var (client, surfaced) = NewClient(js);

		await client.ConnectAsync(server.Uri, Alice);

		await Assert.That(client.Resumed).IsFalse();
		await Assert.That(await Eventually.TrueAsync(() => surfaced.Contains("banner"))).IsTrue();
		js.Flush();
		await Assert.That(await Eventually.TrueAsync(() => js.StoredValue(AliceKey) is { } v && v.Contains("tok-new"))).IsTrue();
		js.Flush();
		await Assert.That(js.StoredValue(AliceKey)).IsEqualTo(Point("tok-new", 1));

		await client.DisposeAsync();
	}

	[Test]
	public async Task A_page_that_was_not_reloaded_says_hello_and_writes_frame_numbers_only_when_staged_ones_flush()
	{
		var js = new FakeResumeJs { Reloaded = false };
		js.Seed(AliceKey, Point("other-tab", 9));
		await using var server = await ScriptedTerminalServer.StartAsync(_ =>
			[Token("tok-1"), Seq(1, "one"), Seq(2, "two"), Seq(3, "three")]);
		var (client, surfaced) = NewClient(js);

		await client.ConnectAsync(server.Uri, Alice);

		var first = await server.FirstFrameAsync();
		await Assert.That(first).Contains("\"type\":\"hello\"");
		await Assert.That(await Eventually.TrueAsync(() => surfaced.Count == 3)).IsTrue();
		await Assert.That(js.StoredValue(AliceKey)).IsEqualTo(Point("tok-1", 0));
		await Assert.That(js.Writes).IsEqualTo(1).Because("frame numbers are staged, never written per frame");
		await Assert.That(js.StagedValue(AliceKey)).IsEqualTo(Point("tok-1", 3));

		await client.DisposeAsync();
	}

	[Test]
	public async Task Another_identity_never_resumes_this_ones_session()
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("alices", 9));
		await using var server = await ScriptedTerminalServer.StartAsync(_ => [Token("bobs")]);
		var (client, _) = NewClient(js);

		await client.ConnectAsync(server.Uri, new TerminalIdentity("bob", "#6:200"));

		var first = await server.FirstFrameAsync();
		await Assert.That(first).Contains("\"type\":\"hello\"");
		await Assert.That(js.StoredValue(AliceKey)).IsEqualTo(Point("alices", 9));

		await client.DisposeAsync();
	}

	[Test]
	public async Task A_connection_without_an_identity_keeps_nothing()
	{
		var js = new FakeResumeJs();
		await using var server = await ScriptedTerminalServer.StartAsync(_ => [Token("guest"), Seq(1, "one")]);
		var (client, surfaced) = NewClient(js);

		await client.ConnectAsync(server.Uri);

		await Assert.That(await Eventually.TrueAsync(() => surfaced.Contains("one"))).IsTrue();
		await Assert.That(js.Writes).IsEqualTo(0);
		js.Flush();
		await Assert.That(js.Writes).IsEqualTo(0);

		await client.DisposeAsync();
	}

	[Test]
	public async Task Bye_forgets_the_stored_point()
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("tok-1", 2));
		await using var server = await ScriptedTerminalServer.StartAsync(_ => [Reattached, Token("tok-2"), Bye]);
		var (client, _) = NewClient(js);

		await client.ConnectAsync(server.Uri, Alice);

		await Assert.That(await Eventually.TrueAsync(() => js.StoredValue(AliceKey) is null)).IsTrue();
		js.Flush();
		await Assert.That(js.StoredValue(AliceKey)).IsNull();

		await client.DisposeAsync();
	}

	/// <summary>
	/// The terminal sends its login token only when the server started a fresh session. Sent to a
	/// resumed one, still logged in, it would run as a command.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task The_login_token_goes_only_to_a_fresh_session(bool resumable)
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("tok-1", 2));
		await using var server = await ScriptedTerminalServer.StartAsync(_ => resumable
			? [Reattached, Token("tok-2")]
			: [Token("tok-fresh")]);
		var terminal = new TerminalService(
			new WebSocketClientService(NullLogger<WebSocketClientService>.Instance, new TerminalResumeStore(js)),
			NullLogger<TerminalService>.Instance);

		await terminal.ConnectWithOttAsync(server.Uri, "the-ott", Alice);
		// Something the client sends after the login, so the server has seen everything before it.
		await terminal.SendAsync("marker");

		await Assert.That(await Eventually.TrueAsync(() => server.LaterFrames.Contains("marker"))).IsTrue();
		await Assert.That(server.LaterFrames.Contains("connect token the-ott")).IsEqualTo(!resumable);

		await terminal.DisposeAsync();
	}
}
