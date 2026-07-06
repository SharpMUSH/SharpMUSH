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
	public async Task SeqEnvelope_IsHello_matches_only_the_structured_handshake()
	{
		await Assert.That(SeqEnvelope.IsHello("{\"hello\":1}")).IsTrue();
		// A real command that merely contains the substring must NOT be read as the handshake.
		await Assert.That(SeqEnvelope.IsHello("say \"hello\"")).IsFalse();
		await Assert.That(SeqEnvelope.IsHello("look north")).IsFalse();
		await Assert.That(SeqEnvelope.IsHello("{\"resume\":\"tok\",\"lastSeq\":0}")).IsFalse();
	}

	[Test]
	public async Task ReplayStore_assigns_monotonic_seq_and_replays_after_lastSeq()
	{
		var store = new TerminalReplayStore();
		var s1 = (await store.AppendAsync("sess", Encoding.UTF8.GetBytes("one"))).Seq;
		var s2 = (await store.AppendAsync("sess", Encoding.UTF8.GetBytes("two"))).Seq;
		var s3 = (await store.AppendAsync("sess", Encoding.UTF8.GetBytes("three"))).Seq;

		await Assert.That(new[] { s1, s2, s3 }).IsEquivalentTo(new[] { 1L, 2L, 3L });

		var replay = await store.AfterAsync("sess", lastSeq: 1);
		await Assert.That(replay.Count).IsEqualTo(2);
		await Assert.That(SeqEnvelope.ReadSeq(replay[0])).IsEqualTo(2L);
	}

	[Test]
	public async Task ReplayStore_evicts_frames_older_than_max_age()
	{
		var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var clock = now;
		var store = new TerminalReplayStore(() => clock);
		await store.AppendAsync("sess", Encoding.UTF8.GetBytes("old"));
		clock = now.AddSeconds(45); // past the 30s window
		await store.AppendAsync("sess", Encoding.UTF8.GetBytes("fresh"));

		var replay = await store.AfterAsync("sess", lastSeq: 0);

		await Assert.That(replay.Count).IsEqualTo(1); // only "fresh" survives the age cutoff
	}

	[Test]
	public async Task ResumeToken_resolves_within_ttl_and_expires_after()
	{
		var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var clock = now;
		var svc = new ResumeTokenService(() => clock);
		var token = await svc.MintAsync(handle: 55, session: "sess");

		var (found1, h1, _) = await svc.TryResolveAsync(token);
		await Assert.That(found1).IsTrue();
		await Assert.That(h1).IsEqualTo(55L);

		clock = now.AddSeconds(31);
		var (found2, _, _) = await svc.TryResolveAsync(token);
		await Assert.That(found2).IsFalse();
	}

	[Test]
	public async Task ReplayStore_isolates_frames_by_session()
	{
		var store = new TerminalReplayStore();
		await store.AppendAsync("session-A", Encoding.UTF8.GetBytes("prior secret"));

		// A different incarnation (e.g. a recycled handle's new occupant) must never see another's frames.
		await Assert.That(await store.AfterAsync("session-B", lastSeq: 0)).IsEmpty();
		await Assert.That((await store.AfterAsync("session-A", lastSeq: 0)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task ReplayStore_Drop_releases_a_sessions_buffer()
	{
		var store = new TerminalReplayStore();
		await store.AppendAsync("sess", Encoding.UTF8.GetBytes("x"));

		await store.DropAsync("sess");

		await Assert.That(await store.AfterAsync("sess", lastSeq: 0)).IsEmpty();
	}

	[Test]
	public async Task ResumeToken_carries_both_handle_and_session()
	{
		var svc = new ResumeTokenService();
		var token = await svc.MintAsync(handle: 7, session: "session-A");

		var (found, handle, session) = await svc.TryResolveAsync(token);
		await Assert.That(found).IsTrue();
		await Assert.That(handle).IsEqualTo(7L);
		await Assert.That(session).IsEqualTo("session-A");
	}
}
