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

	/// <summary>The token and lastSeq a resume frame asked for.</summary>
	private static TerminalResumePoint ResumePointOf(string frame)
	{
		using var document = JsonDocument.Parse(frame);
		var root = document.RootElement;
		return new TerminalResumePoint(root.GetProperty("token").GetString()!, root.GetProperty("lastSeq").GetInt64());
	}

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

	private static readonly string AliceLinesKey = TerminalResumeStore.LinesKeyFor("play", Alice);

	private static string StoredLines(params string[] texts) =>
		"[" + string.Join(",", texts.Select(t => JsonSerializer.Serialize(new { t, h = t, at = 0L }))) + "]";

	private static string[] ServerTexts(TerminalService terminal) =>
		terminal.Lines.Where(l => l.Source == SharpMUSH.Client.Models.TerminalLineSource.Server).Select(l => l.Text).ToArray();

	private static TerminalService NewTerminal(FakeResumeJs js) => new(
		new WebSocketClientService(NullLogger<WebSocketClientService>.Instance, new TerminalResumeStore(js)),
		NullLogger<TerminalService>.Instance);

	private static string[] StoredTexts(FakeResumeJs js) =>
		js.StoredValue(AliceLinesKey) is { } stored
			? JsonDocument.Parse(stored).RootElement.EnumerateArray().Select(l => l.GetProperty("t").GetString()!).ToArray()
			: [];

	/// <summary>
	/// The terminal keeps a line just after it shows it, on the receive thread, so a test that saw the line
	/// waits for the stored screen to catch up (flushing the page timer each time).
	/// </summary>
	private static Task<bool> StoredTextsBecomeAsync(FakeResumeJs js, params string[] expected) =>
		Eventually.TrueAsync(() =>
		{
			js.Flush();
			return StoredTexts(js).SequenceEqual(expected);
		});

	/// <summary>
	/// A reload that resumes shows the screen it had, then the frames it missed: the stored lines come
	/// back before the replay, and new lines carry on after them in storage.
	/// </summary>
	[Test]
	public async Task A_resumed_reload_restores_the_screen_before_the_replayed_frames()
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("tok-1", 5));
		js.Seed(AliceLinesKey, StoredLines("old one", "old two"));
		await using var server = await ScriptedTerminalServer.StartAsync(_ => [Reattached, Seq(6, "six"), Token("tok-2")]);
		var terminal = NewTerminal(js);

		await terminal.ConnectWithOttAsync(server.Uri, "the-ott", Alice);

		await Assert.That(await Eventually.TrueAsync(() => ServerTexts(terminal).Contains("six"))).IsTrue();
		await Assert.That(ServerTexts(terminal)).IsEquivalentTo(["old one", "old two", "six"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		var lines = terminal.Lines.Select(l => l.Text).ToList();
		await Assert.That(lines.IndexOf("old two")).IsLessThan(lines.FindIndex(l => l.StartsWith("Session resumed", StringComparison.Ordinal)));

		await Assert.That(await StoredTextsBecomeAsync(js, "old one", "old two", "six")).IsTrue();

		await terminal.DisposeAsync();
	}

	/// <summary>
	/// A refused resume is a new session, but the same character in the same tab: the screen the reloaded
	/// page left comes back above a line saying the session started again, then the new session's lines, and
	/// all of it is kept for the next reload.
	/// </summary>
	[Test]
	public async Task A_refused_resume_restores_the_old_screen_above_the_new_session()
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("expired", 40));
		js.Seed(AliceLinesKey, StoredLines("old one"));
		await using var server = await ScriptedTerminalServer.StartAsync(_ => [Seq(1, "banner"), Token("tok-new")]);
		var terminal = NewTerminal(js);

		await terminal.ConnectWithOttAsync(server.Uri, "the-ott", Alice);

		await Assert.That(await Eventually.TrueAsync(() => ServerTexts(terminal).Contains("banner"))).IsTrue();
		await Assert.That(ServerTexts(terminal)).IsEquivalentTo(["old one", "banner"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		var lines = terminal.Lines.Select(l => l.Text).ToList();
		var divider = lines.FindIndex(l => l.Contains("The session started again", StringComparison.Ordinal));
		await Assert.That(divider).IsGreaterThan(lines.IndexOf("old one"));
		await Assert.That(divider).IsLessThan(lines.IndexOf("banner"));
		await Assert.That(await StoredTextsBecomeAsync(js, "old one", "banner")).IsTrue();

		await terminal.DisposeAsync();
	}

	/// <summary>A fresh session on a page that was not reloaded does not carry a stale screen forward.</summary>
	[Test]
	public async Task A_fresh_session_starts_its_stored_screen_empty()
	{
		var js = new FakeResumeJs { Reloaded = false };
		js.Seed(AliceLinesKey, StoredLines("stale"));
		await using var server = await ScriptedTerminalServer.StartAsync(_ => [Token("tok-1"), Seq(1, "welcome")]);
		var terminal = NewTerminal(js);

		await terminal.ConnectWithOttAsync(server.Uri, "the-ott", Alice);

		await Assert.That(await Eventually.TrueAsync(() => ServerTexts(terminal).Contains("welcome"))).IsTrue();
		await Assert.That(await StoredTextsBecomeAsync(js, "welcome")).IsTrue();

		await terminal.DisposeAsync();
	}

	/// <summary>
	/// An OOB frame is data, not screen: it never becomes a stored line, so restoring the screen cannot
	/// run it again. Its package reaches the store only when the server sends it.
	/// </summary>
	[Test]
	public async Task Out_of_band_frames_are_never_stored_as_scrollback()
	{
		var js = new FakeResumeJs { Reloaded = false };
		var oob = JsonSerializer.Serialize(new { type = "oob", package = "room.info", data = new { name = "Quay" } });
		await using var server = await ScriptedTerminalServer.StartAsync(_ => [Token("tok-1"), Seq(1, oob), Seq(2, "text")]);
		var terminal = NewTerminal(js);

		await terminal.ConnectWithOttAsync(server.Uri, "the-ott", Alice);

		await Assert.That(await Eventually.TrueAsync(() => ServerTexts(terminal).Contains("text"))).IsTrue();
		await Assert.That(terminal.OobChannels.Get("room.info")).IsNotNull();
		await Assert.That(await StoredTextsBecomeAsync(js, "text")).IsTrue();
		await Assert.That(js.StoredValue(AliceLinesKey)).DoesNotContain("room.info");

		await terminal.DisposeAsync();
	}

	/// <summary>
	/// A socket that drops before the server answers the resume says nothing about the session: it may
	/// still be there to rebind. The client resumes again with the same token and lastSeq, and the login
	/// token is never sent, so it can never reach the session as a command (PR #1488 review).
	/// </summary>
	[Test]
	public async Task An_interrupted_resume_is_retried_with_the_same_point_and_never_sends_the_login()
	{
		var js = new FakeResumeJs();
		js.Seed(AliceKey, Point("tok-1", 5));
		js.Seed(AliceLinesKey, StoredLines("old one"));
		await using var server = await ScriptedTerminalServer.StartAsync((_, connection) => connection == 1
			? null
			: [Reattached, Seq(6, "six"), Token("tok-2")]);
		var terminal = NewTerminal(js);

		await terminal.ConnectWithOttAsync(server.Uri, "the-ott", Alice);
		await terminal.SendAsync("marker");

		await Assert.That(await Eventually.TrueAsync(() => server.LaterFrames.Contains("marker"))).IsTrue();
		var resumedFrom = server.FirstFrames.Select(ResumePointOf).ToArray();
		await Assert.That(resumedFrom).IsEquivalentTo([new TerminalResumePoint("tok-1", 5), new TerminalResumePoint("tok-1", 5)]);
		await Assert.That(server.LaterFrames.Any(f => f.StartsWith("connect token", StringComparison.Ordinal))).IsFalse();
		await Assert.That(ServerTexts(terminal)).IsEquivalentTo(["old one", "six"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await Eventually.TrueAsync(() => js.StoredValue(AliceKey) == Point("tok-2", 6))).IsTrue();

		await terminal.DisposeAsync();
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
