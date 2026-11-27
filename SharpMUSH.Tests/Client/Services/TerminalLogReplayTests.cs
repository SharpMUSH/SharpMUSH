using Microsoft.JSInterop;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The saved log after a reload that resumes: the frames the server replays from a point that lagged, which the
/// page before the reload had logged already, are not logged a second time.
/// </summary>
public class TerminalLogReplayTests
{
	private static readonly TerminalIdentity Alice = new("alice", "#5:100");

	/// <summary><see cref="FakeResumeJs"/>, plus the saved log's calls: the lines appended, and the log left on.</summary>
	private sealed class LogJs : IJSRuntime
	{
		public FakeResumeJs Resume { get; } = new();
		public List<string> Logged { get; } = [];

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
		{
			switch (identifier)
			{
				case "localStorage.getItem":
					return new ValueTask<TValue>(default(TValue)!);
				case "SharpMUSH.TerminalLog.append":
					Logged.Add((string)args![2]!);
					return new ValueTask<TValue>(default(TValue)!);
				default:
					return Resume.InvokeAsync<TValue>(identifier, args);
			}
		}

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
			=> InvokeAsync<TValue>(identifier, args);
	}

	private static TerminalLine Line(string text, long seq) => new(DateTime.Now, text, TerminalLineSource.Server) { Seq = seq };

	[Test]
	public async Task Frames_logged_before_a_reload_are_not_logged_again_when_replayed()
	{
		var js = new LogJs();
		var store = new TerminalResumeStore(js);
		var log = new TerminalLog(js);
		// The page before the reload: its staged point lagged at frame 3, but it had logged frames 4 and 5.
		var before = await store.OpenAsync("play", Alice);
		await before.SaveAsync(new TerminalResumePoint("tok", 3));
		await log.AppendAsync(Alice, Line("Ann waves.", 4), before);
		await log.AppendAsync(Alice, Line("Ann bows.", 5), before);

		var slot = await store.OpenAsync("play", Alice);
		// The server replays 4 and 5 from the lagging point; frame 6 is new and holds two lines.
		await log.AppendAsync(Alice, Line("Ann waves.", 4), slot);
		await log.AppendAsync(Alice, Line("Ann bows.", 5), slot);
		await log.AppendAsync(Alice, Line("Ann leaves.", 6), slot);
		await log.AppendAsync(Alice, Line("The door shuts.", 6), slot);

		await Assert.That(js.Logged.Count).IsEqualTo(4);
		await Assert.That(js.Logged.Count(l => l.Contains("Ann waves."))).IsEqualTo(1);
		await Assert.That(js.Logged.Count(l => l.Contains("Ann bows."))).IsEqualTo(1);
		await Assert.That(js.Logged.Any(l => l.Contains("The door shuts."))).IsTrue();
		await Assert.That(js.Resume.StoredValue(TerminalResumeStore.LoggedKeyFor("play", Alice))).IsEqualTo("6");
	}

	[Test]
	public async Task The_logged_frame_is_written_at_once_not_staged()
	{
		var js = new LogJs();
		var log = new TerminalLog(js);
		var slot = await new TerminalResumeStore(js).OpenAsync("play", Alice);

		await log.AppendAsync(Alice, Line("Ann waves.", 2), slot);

		// No Flush: a page killed now still knows frame 2 is in the log.
		await Assert.That(js.Resume.StoredValue(TerminalResumeStore.LoggedKeyFor("play", Alice))).IsEqualTo("2");
	}

	[Test]
	public async Task A_fresh_session_logs_from_frame_one_again()
	{
		var js = new LogJs();
		var store = new TerminalResumeStore(js);
		var log = new TerminalLog(js);
		var before = await store.OpenAsync("play", Alice);
		await before.SaveAsync(new TerminalResumePoint("tok", 3));
		await log.AppendAsync(Alice, Line("Ann waves.", 40), before);

		var slot = await store.OpenAsync("play", Alice);
		// The server refused the resume: the client clears the slot, and the new session starts at frame 1.
		await slot.ClearAsync();
		await log.AppendAsync(Alice, Line("Welcome.", 1), slot);

		await Assert.That(js.Logged.Any(l => l.Contains("Welcome."))).IsTrue();
		await Assert.That(js.Resume.StoredValue(TerminalResumeStore.LoggedKeyFor("play", Alice))).IsEqualTo("1");
	}

	[Test]
	public async Task A_line_from_no_numbered_frame_is_always_logged()
	{
		var js = new LogJs();
		var store = new TerminalResumeStore(js);
		var log = new TerminalLog(js);
		var before = await store.OpenAsync("play", Alice);
		await before.SaveAsync(new TerminalResumePoint("tok", 3));
		await log.AppendAsync(Alice, Line("Ann waves.", 9), before);

		var slot = await store.OpenAsync("play", Alice);
		await log.AppendAsync(Alice, Line("Server banner", 0), slot);

		await Assert.That(js.Logged.Any(l => l.Contains("Server banner"))).IsTrue();
	}
}
