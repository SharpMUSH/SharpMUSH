using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// <see cref="TerminalService.Prompt"/>: the prompt the game is waiting on is kept apart from the scrollback,
/// replaced by the next one, cleared by its session's clear, and copied into the scrollback when answered.
/// </summary>
public class TerminalServicePromptTests
{
	private static readonly TerminalIdentity Alice = new("alice", "#5:100");

	private static async Task<(TerminalService Svc, IWebSocketClientService Ws)> ConnectedAsync()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var svc = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>());
		await svc.ConnectAsync("ws://localhost:4202/ws");
		return (svc, ws);
	}

	private static string PromptFrame(string text, string? session = null)
	{
		var envelope = new JsonObject
		{
			["type"] = "markup",
			["data"] = MarkupTextSerializer.Serialize(MString.Plain(text)),
			["prompt"] = true
		};
		if (session is not null) envelope["session"] = session;
		return envelope.ToJsonString();
	}

	private static string ClearFrame(string? session = null) => session is null
		? "{\"type\":\"prompt\",\"clear\":true}"
		: $"{{\"type\":\"prompt\",\"clear\":true,\"session\":\"{session}\"}}";

	private static string LineFrame(string text) => new JsonObject
	{
		["type"] = "markup",
		["data"] = MarkupTextSerializer.Serialize(MString.Plain(text))
	}.ToJsonString();

	private static void Receive(IWebSocketClientService ws, string frame) =>
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws, frame);

	private static string[] Texts(TerminalService svc, TerminalLineSource source) =>
		[.. svc.Lines.Where(l => l.Source == source).Select(l => l.Text)];

	[Test]
	public async Task A_prompt_frame_becomes_the_prompt_and_not_a_line()
	{
		var (svc, ws) = await ConnectedAsync();
		var changes = 0;
		svc.PromptChanged += () => changes++;

		Receive(ws, PromptFrame("Read which post?", "s1"));

		await Assert.That(svc.Prompt?.Line.Text).IsEqualTo("Read which post?");
		await Assert.That(svc.Prompt?.Session).IsEqualTo("s1");
		await Assert.That(svc.Prompt?.Line.Source).IsEqualTo(TerminalLineSource.Server);
		await Assert.That(Texts(svc, TerminalLineSource.Server)).IsEmpty();
		await Assert.That(changes).IsEqualTo(1);
	}

	[Test]
	public async Task A_second_prompt_replaces_the_first()
	{
		var (svc, ws) = await ConnectedAsync();

		Receive(ws, PromptFrame("First?", "s1"));
		Receive(ws, PromptFrame("Second?"));

		await Assert.That(svc.Prompt?.Line.Text).IsEqualTo("Second?");
		await Assert.That(svc.Prompt?.Session).IsEqualTo(string.Empty);
		await Assert.That(Texts(svc, TerminalLineSource.Server)).IsEmpty();
	}

	[Test]
	[Arguments("s1")]
	[Arguments(null)]
	public async Task A_clear_for_the_shown_session_or_naming_none_empties_the_prompt(string? clearSession)
	{
		var (svc, ws) = await ConnectedAsync();
		Receive(ws, PromptFrame("Read which post?", "s1"));
		var changes = 0;
		svc.PromptChanged += () => changes++;

		Receive(ws, ClearFrame(clearSession));

		await Assert.That(svc.Prompt).IsNull();
		await Assert.That(changes).IsEqualTo(1);
	}

	[Test]
	public async Task A_clear_for_another_session_is_ignored()
	{
		var (svc, ws) = await ConnectedAsync();
		Receive(ws, PromptFrame("New session?", "s2"));
		var changes = 0;
		svc.PromptChanged += () => changes++;

		Receive(ws, ClearFrame("s1"));

		await Assert.That(svc.Prompt?.Line.Text).IsEqualTo("New session?");
		await Assert.That(changes).IsEqualTo(0);
	}

	[Test]
	public async Task Ordinary_output_leaves_the_prompt_alone()
	{
		var (svc, ws) = await ConnectedAsync();
		Receive(ws, PromptFrame("Read which post?", "s1"));

		Receive(ws, LineFrame("Someone arrives."));

		await Assert.That(svc.Prompt?.Line.Text).IsEqualTo("Read which post?");
		await Assert.That(Texts(svc, TerminalLineSource.Server)).IsEquivalentTo(["Someone arrives."]);
	}

	[Test]
	public async Task Sending_copies_a_one_off_prompt_into_the_scrollback_and_clears_it()
	{
		var (svc, ws) = await ConnectedAsync();
		Receive(ws, PromptFrame("Continue?"));

		await svc.SendAsync("yes");

		await Assert.That(svc.Prompt).IsNull();
		await Assert.That(svc.Lines.Select(l => (l.Source, l.Text)).TakeLast(2)).IsEquivalentTo(
			[(TerminalLineSource.Server, "Continue?"), (TerminalLineSource.Client, "yes")],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await ws.Received(1).SendAsync("yes");
	}

	[Test]
	public async Task Sending_copies_a_session_prompt_into_the_scrollback_and_keeps_it()
	{
		var (svc, ws) = await ConnectedAsync();
		Receive(ws, PromptFrame("Read which post?", "s1"));

		await svc.SendAsync("2");
		await svc.SendAsync("3");

		await Assert.That(svc.Prompt?.Line.Text).IsEqualTo("Read which post?");
		await Assert.That(svc.Lines.Select(l => l.Text).TakeLast(4)).IsEquivalentTo(
			["Read which post?", "2", "Read which post?", "3"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Sending_with_no_prompt_adds_only_the_line()
	{
		var (svc, _) = await ConnectedAsync();

		await svc.SendAsync("look");

		await Assert.That(Texts(svc, TerminalLineSource.Server)).IsEmpty();
		await Assert.That(Texts(svc, TerminalLineSource.Client)).IsEquivalentTo(["look"]);
	}

	[Test]
	public async Task Queries_and_control_frames_leave_the_prompt_alone()
	{
		var (svc, ws) = await ConnectedAsync();
		Receive(ws, PromptFrame("Continue?"));

		await svc.SendCommandAsync("lcon(me)", 50);
		await svc.SendControlAsync("{\"type\":\"naws\",\"cols\":80,\"rows\":24}");

		await Assert.That(svc.Prompt?.Line.Text).IsEqualTo("Continue?");
		await Assert.That(Texts(svc, TerminalLineSource.Server)).IsEmpty();
	}

	[Test]
	public async Task Disconnecting_clears_the_prompt()
	{
		var (svc, ws) = await ConnectedAsync();
		Receive(ws, PromptFrame("Read which post?", "s1"));

		await svc.DisconnectAsync();

		await Assert.That(svc.Prompt).IsNull();
	}

	/// <summary>
	/// The prompt is kept beside the scrollback, so a reload that resumes shows it again; the server then
	/// clears it if its session ended meanwhile.
	/// </summary>
	[Test]
	public async Task A_resumed_reload_shows_the_prompt_the_page_kept()
	{
		var js = new FakeResumeJs();
		var store = new TerminalResumeStore(js);
		var key = TerminalResumeStore.KeyFor("play", Alice);
		var promptKey = TerminalResumeStore.PromptKeyFor("play", Alice);

		// The page before the reload: it shows a prompt, which is staged for the page's timer.
		var (before, beforeWs) = await ConnectedAsync();
		var beforeSlot = await store.OpenAsync("play", Alice);
		beforeWs.ResumeSlot.Returns(beforeSlot);
		await beforeSlot.SaveAsync(new TerminalResumePoint("tok-1", 3));
		Receive(beforeWs, PromptFrame("Read which post?", "s1"));
		js.Flush();
		await Assert.That(js.StoredValue(promptKey)).IsNotNull();

		// The reloaded page resumes the session.
		var (after, afterWs) = await ConnectedAsync();
		var afterSlot = await store.OpenAsync("play", Alice);
		afterWs.ResumeSlot.Returns(afterSlot);
		afterWs.Reattached += Raise.Event<EventHandler>(afterWs, EventArgs.Empty);

		await Assert.That(after.Prompt?.Line.Text).IsEqualTo("Read which post?");
		await Assert.That(after.Prompt?.Session).IsEqualTo("s1");
		await Assert.That(after.Lines.Any(l => l.Text == "Read which post?")).IsFalse();

		// Its session had ended: the server's clear empties the row and forgets the kept prompt.
		Receive(afterWs, ClearFrame("s1"));
		js.Flush();
		await Assert.That(after.Prompt).IsNull();
		await Assert.That(js.StoredValue(promptKey)).IsNull();
		await Assert.That(js.StoredValue(key)).IsNotNull();
		await Assert.That(before.Prompt).IsNotNull();
	}

	[Test]
	public async Task A_refused_resume_does_not_bring_the_old_prompt_back()
	{
		var js = new FakeResumeJs();
		var store = new TerminalResumeStore(js);
		js.Seed(TerminalResumeStore.KeyFor("play", Alice), "{\"token\":\"tok-1\",\"lastSeq\":3}");
		js.Seed(TerminalResumeStore.PromptKeyFor("play", Alice),
			TerminalScrollback.SerializePrompt(new TerminalPrompt(
				new TerminalLine(DateTime.Now, "Old?", TerminalLineSource.Server), "s1")).Expect<string>());

		var (svc, ws) = await ConnectedAsync();
		var slot = await store.OpenAsync("play", Alice);
		ws.ResumeSlot.Returns(slot);
		ws.ResumeRefused += Raise.Event<EventHandler>(ws, EventArgs.Empty);

		await Assert.That(svc.Prompt).IsNull();
		await Assert.That(slot.TakePrompt() is NotFound).IsTrue();
	}
}
