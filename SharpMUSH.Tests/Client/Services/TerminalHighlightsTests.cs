using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// <see cref="TerminalHighlights"/> through the play terminal: every matching server line is coloured, and a frame
/// plays its sound or raises its notification once, however it comes back (a reconnect's replay, a reload's
/// restored screen, a reload that replays from a point that lagged).
/// </summary>
public class TerminalHighlightsTests
{
	private static readonly TerminalIdentity Alice = new("alice", "#5:100");

	/// <summary>Records the alert calls <c>js/terminal-alerts.js</c> would get.</summary>
	private sealed class FakeAlertsJs : IJSRuntime
	{
		public List<(string Call, object?[] Args)> Calls { get; } = [];

		public int Count(string call) => Calls.Count(c => c.Call == call);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
		{
			Calls.Add((identifier, args ?? []));
			object? result = identifier == "SharpMUSH.Alerts.permission" ? "granted" : true;
			return new ValueTask<TValue>((TValue)result!);
		}

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
			=> InvokeAsync<TValue>(identifier, args);
	}

	private sealed class Rules(params HighlightRule[] rules) : IHighlightMatcherSource
	{
		public HighlightMatcher Matcher { get; } = new(rules);
	}

	private static readonly HighlightRule AnnRule =
		new("r1", "Ann", Colour: HighlightColour.Warn, Sound: HighlightSound.Chime, Notify: true);

	private sealed record Rig(TerminalService Terminal, IWebSocketClientService Ws, FakeAlertsJs Alerts);

	private static async Task<Rig> ConnectedAsync(TerminalResumeSlot? slot = null, params HighlightRule[] rules)
	{
		var ws = Substitute.For<IWebSocketClientService>();
		ws.ResumeSlot.Returns(slot);
		var alerts = new FakeAlertsJs();
		var highlights = new TerminalHighlights(new Lazy<IHighlightMatcherSource>(new Rules(rules.Length == 0 ? [AnnRule] : rules)),
			new Lazy<TerminalAlerts>(new TerminalAlerts(alerts)));
		var svc = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>(), highlights: highlights);
		await svc.ConnectAsync("ws://localhost:4202/ws");
		svc.ConnectedPlayerName = "Bob";
		return new Rig(svc, ws, alerts);
	}

	private static string LineFrame(string text, string? comm = null)
	{
		var frame = new JsonObject
		{
			["type"] = "markup",
			["data"] = JsonNode.Parse(MarkupTextSerializer.Serialize(MString.Plain(text)))
		};
		if (comm is not null) frame["comm"] = comm;
		return frame.ToJsonString();
	}

	/// <summary>Delivers <paramref name="frame"/> as frame number <paramref name="seq"/>, as the client does.</summary>
	private static void Receive(Rig rig, long seq, string frame)
	{
		rig.Ws.FrameSeq.Returns(seq);
		rig.Ws.MessageReceived += Raise.Event<EventHandler<string>>(rig.Ws, frame);
		rig.Ws.FrameSeq.Returns(0L);
	}

	private static TerminalLine Last(Rig rig) => rig.Terminal.Lines.Last(l => l.Source == TerminalLineSource.Server);

	[Test]
	public async Task A_matching_line_is_coloured_and_alerts()
	{
		var rig = await ConnectedAsync();

		Receive(rig, 1, LineFrame("Ann waves to you."));

		await Assert.That(Last(rig).Highlight.Colour).IsEqualTo(HighlightColour.Warn);
		await Assert.That(Last(rig).Seq).IsEqualTo(1L);
		await Assert.That(rig.Alerts.Count("SharpMUSH.Alerts.play")).IsEqualTo(1);
		var notify = rig.Alerts.Calls.Single(c => c.Call == "SharpMUSH.Alerts.notify");
		await Assert.That(notify.Args[0]).IsEqualTo("Bob");
		await Assert.That(notify.Args[1]).IsEqualTo("Ann waves to you.");
		await Assert.That(notify.Args[2]).IsEqualTo("sharpmush-hl-r1");
	}

	[Test]
	public async Task A_line_no_rule_matches_is_left_alone()
	{
		var rig = await ConnectedAsync();

		Receive(rig, 1, LineFrame("Bob waves."));

		await Assert.That(Last(rig).Highlight.Colour).IsEqualTo(HighlightColour.None);
		await Assert.That(rig.Alerts.Calls).IsEmpty();
	}

	[Test]
	public async Task A_rule_for_pages_sees_the_engines_mark()
	{
		var rig = await ConnectedAsync(null, AnnRule with { Lines = HighlightLines.Pages });

		Receive(rig, 1, LineFrame("Ann says, \"hi\""));
		Receive(rig, 2, LineFrame("Ann pages: hi", "page"));

		var lines = rig.Terminal.Lines.Where(l => l.Source == TerminalLineSource.Server).ToArray();
		await Assert.That(lines[0].Highlight.Colour).IsEqualTo(HighlightColour.None);
		await Assert.That(lines[1].Highlight.Colour).IsEqualTo(HighlightColour.Warn);
		await Assert.That(rig.Alerts.Count("SharpMUSH.Alerts.play")).IsEqualTo(1);
	}

	[Test]
	public async Task The_players_own_commands_never_match()
	{
		var rig = await ConnectedAsync();

		await rig.Terminal.SendAsync("page Ann=hello");

		await Assert.That(rig.Terminal.Lines.Single(l => l.Source == TerminalLineSource.Client).Highlight.Colour).IsEqualTo(HighlightColour.None);
		await Assert.That(rig.Alerts.Calls).IsEmpty();
	}

	[Test]
	public async Task One_frame_of_several_lines_alerts_once()
	{
		var rig = await ConnectedAsync();

		Receive(rig, 7, "Ann arrives.\nAnn waves.\nAnn sits.");

		await Assert.That(rig.Terminal.Lines.Count(l => l.Highlight.Colour == HighlightColour.Warn)).IsEqualTo(3);
		await Assert.That(rig.Alerts.Count("SharpMUSH.Alerts.play")).IsEqualTo(1);
		await Assert.That(rig.Alerts.Count("SharpMUSH.Alerts.notify")).IsEqualTo(1);
	}

	[Test]
	public async Task A_line_from_no_numbered_frame_is_coloured_but_does_not_alert()
	{
		var rig = await ConnectedAsync();

		Receive(rig, 0, LineFrame("Ann waves."));

		await Assert.That(Last(rig).Highlight.Colour).IsEqualTo(HighlightColour.Warn);
		await Assert.That(rig.Alerts.Calls).IsEmpty();
	}

	[Test]
	public async Task A_frame_already_alerted_for_before_a_reload_does_not_alert_again()
	{
		var js = new FakeResumeJs();
		var store = new TerminalResumeStore(js);
		// The page before the reload: the point it staged lagged at frame 3, but it alerted for frame 5.
		var before = await store.OpenAsync("play", Alice);
		await before.SaveAsync(new TerminalResumePoint("tok", 3));
		await Assert.That(before.TryClaimAlert(5)).IsTrue();

		var slot = await store.OpenAsync("play", Alice);
		var rig = await ConnectedAsync(slot);

		// The server replays 4 and 5 from the lagging point, then 6 is new.
		Receive(rig, 4, LineFrame("Ann waves."));
		Receive(rig, 5, LineFrame("Ann bows."));
		await Assert.That(rig.Alerts.Calls).IsEmpty();
		await Assert.That(Last(rig).Highlight.Colour).IsEqualTo(HighlightColour.Warn);

		Receive(rig, 6, LineFrame("Ann leaves."));
		await Assert.That(rig.Alerts.Count("SharpMUSH.Alerts.play")).IsEqualTo(1);
		await Assert.That(js.StoredValue(TerminalResumeStore.AlertedKeyFor("play", Alice))).IsEqualTo("6");
	}

	[Test]
	public async Task The_alerted_frame_is_written_at_once_not_staged()
	{
		var js = new FakeResumeJs();
		var slot = await new TerminalResumeStore(js).OpenAsync("play", Alice);
		var rig = await ConnectedAsync(slot);

		Receive(rig, 2, LineFrame("Ann waves."));

		// No Flush: a page killed now still knows frame 2 alerted.
		await Assert.That(js.StoredValue(TerminalResumeStore.AlertedKeyFor("play", Alice))).IsEqualTo("2");
	}

	[Test]
	public async Task A_fresh_session_numbers_from_one_and_alerts_again()
	{
		var js = new FakeResumeJs();
		var slot = await new TerminalResumeStore(js).OpenAsync("play", Alice);
		await Assert.That(slot.TryClaimAlert(40)).IsTrue();

		// The server refused the resume: the client clears the slot, and the new session starts at frame 1.
		await slot.ClearAsync();
		var rig = await ConnectedAsync(slot);
		Receive(rig, 1, LineFrame("Ann waves."));

		await Assert.That(rig.Alerts.Count("SharpMUSH.Alerts.play")).IsEqualTo(1);
		await Assert.That(js.StoredValue(TerminalResumeStore.AlertedKeyFor("play", Alice))).IsEqualTo("1");
	}

	[Test]
	public async Task A_restored_screen_is_coloured_and_never_alerts()
	{
		var js = new FakeResumeJs();
		var store = new TerminalResumeStore(js);
		var before = await store.OpenAsync("play", Alice);
		await before.SaveAsync(new TerminalResumePoint("tok", 9));
		await before.AppendLineAsync(new TerminalLine(DateTime.Now, "Ann waves.", TerminalLineSource.Server) { Seq = 9 });
		js.Flush();

		var slot = await store.OpenAsync("play", Alice);
		var rig = await ConnectedAsync(slot);
		rig.Ws.Reattached += Raise.Event<EventHandler>(rig.Ws, EventArgs.Empty);

		await Assert.That(Last(rig).Text).IsEqualTo("Ann waves.");
		await Assert.That(Last(rig).Highlight.Colour).IsEqualTo(HighlightColour.Warn);
		await Assert.That(rig.Alerts.Calls).IsEmpty();
	}

	[Test]
	public async Task A_long_line_is_cut_for_the_notification()
	{
		var body = TerminalAlerts.Body("Ann   says,\n" + new string('x', 400));

		await Assert.That(body.Length).IsEqualTo(TerminalAlerts.MaxBodyLength);
		await Assert.That(body).StartsWith("Ann says, x");
		await Assert.That(body).EndsWith("…");
	}

	[Test]
	public async Task A_rule_that_targets_the_match_marks_it_in_the_shown_html()
	{
		var rule = new HighlightRule("m", "Ann", Colour: HighlightColour.Info, Target: HighlightTarget.Match);
		var rig = await ConnectedAsync(null, rule);

		Receive(rig, 1, LineFrame("Bob waves to Ann."));

		var line = Last(rig);
		await Assert.That(line.Highlight.IsNone).IsTrue();
		await Assert.That(line.ShownHtml).IsEqualTo("Bob waves to <span class=\"terminal-mark terminal-hl-info\">Ann</span>.");
		await Assert.That(line.Text).IsEqualTo("Bob waves to Ann.");
	}

	[Test]
	public async Task A_hidden_line_is_kept_and_still_alerts()
	{
		var rule = new HighlightRule("h", "spam", Colour: HighlightColour.None, Sound: HighlightSound.Ping, Hide: true);
		var rig = await ConnectedAsync(null, rule);

		Receive(rig, 1, LineFrame("<Public> spam spam"));

		await Assert.That(Last(rig).Hidden).IsTrue();
		await Assert.That(rig.Alerts.Count("SharpMUSH.Alerts.play")).IsEqualTo(1);
	}
}
