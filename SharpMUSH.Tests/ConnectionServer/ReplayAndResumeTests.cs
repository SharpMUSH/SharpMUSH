using System.Text;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class ReplayAndResumeTests
{
	[Test]
	public async Task SeqEnvelope_roundtrips_seq_and_data()
	{
		var frame = SeqEnvelope.Wrap(5, "hello\nworld");
		await Assert.That(SeqEnvelope.ReadSeq(frame)).IsEqualTo(5L);
		await Assert.That(Encoding.UTF8.GetString(frame)).Contains("\"seq\":5");
	}

	[Test]
	public async Task SeqEnvelope_parses_resume_frame_camelCase()
	{
		var ok = SeqEnvelope.TryReadResume("{\"resume\":\"tok123\",\"lastSeq\":7}", out var token, out var last);
		await Assert.That(ok).IsTrue();
		await Assert.That(token).IsEqualTo("tok123");
		await Assert.That(last).IsEqualTo(7L);
	}

	[Test]
	public async Task SeqEnvelope_rejects_non_resume_frame()
	{
		var ok = SeqEnvelope.TryReadResume("look north", out _, out _);
		await Assert.That(ok).IsFalse();
	}

	[Test]
	public async Task ReplayStore_assigns_monotonic_seq_and_replays_after_lastSeq()
	{
		var store = new TerminalReplayStore();
		var s1 = store.Append(9, Encoding.UTF8.GetBytes("one")).Seq;
		var s2 = store.Append(9, Encoding.UTF8.GetBytes("two")).Seq;
		var s3 = store.Append(9, Encoding.UTF8.GetBytes("three")).Seq;

		await Assert.That(new[] { s1, s2, s3 }).IsEquivalentTo(new[] { 1L, 2L, 3L });

		var replay = store.After(9, lastSeq: 1);
		await Assert.That(replay.Count).IsEqualTo(2);
		await Assert.That(SeqEnvelope.ReadSeq(replay[0])).IsEqualTo(2L);
	}

	[Test]
	public async Task ReplayStore_evicts_frames_older_than_max_age()
	{
		var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var clock = now;
		var store = new TerminalReplayStore(() => clock);
		store.Append(1, Encoding.UTF8.GetBytes("old"));
		clock = now.AddSeconds(45); // past the 30s window
		store.Append(1, Encoding.UTF8.GetBytes("fresh"));

		var replay = store.After(1, lastSeq: 0);

		await Assert.That(replay.Count).IsEqualTo(1); // only "fresh" survives the age cutoff
	}

	[Test]
	public async Task ResumeToken_resolves_within_ttl_and_expires_after()
	{
		var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var clock = now;
		var svc = new ResumeTokenService(() => clock);
		var token = svc.Mint(handle: 55);

		await Assert.That(svc.TryResolve(token, out var h1)).IsTrue();
		await Assert.That(h1).IsEqualTo(55L);

		clock = now.AddSeconds(31);
		await Assert.That(svc.TryResolve(token, out _)).IsFalse();
	}
}
