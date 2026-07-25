using System.Text;
using SharpMUSH.Client.Services;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ClientState;

public class ResumeFrameParserTests
{
	// --- Cross-contract: the client builders and the server codec must agree on the wire format. ---

	[Test]
	public async Task Client_resume_frame_is_parsed_by_the_server()
	{
		var frame = ResumeFrameParser.Resume("TOK123", lastSeq: 7);
		var ok = SeqEnvelope.TryReadResume(frame, out var token, out var lastSeq);
		await Assert.That(ok).IsTrue();
		await Assert.That(token).IsEqualTo("TOK123");
		await Assert.That(lastSeq).IsEqualTo(7L);
	}

	[Test]
	public async Task Server_resumeToken_frame_is_parsed_by_the_client()
	{
		var frame = Encoding.UTF8.GetString(SeqEnvelope.ResumeToken("TOK123"));
		var ok = ResumeFrameParser.TryReadResumeToken(frame, out var token);
		await Assert.That(ok).IsTrue();
		await Assert.That(token).IsEqualTo("TOK123");
	}

	[Test]
	public async Task Server_reattached_frame_is_recognised_by_the_client()
	{
		var frame = Encoding.UTF8.GetString(SeqEnvelope.Reattached());
		await Assert.That(ResumeFrameParser.IsReattached(frame)).IsTrue();
	}

	[Test]
	public async Task Server_bye_frame_is_recognised_by_the_client()
	{
		var frame = Encoding.UTF8.GetString(SeqEnvelope.Bye());
		await Assert.That(ResumeFrameParser.IsBye(frame)).IsTrue();
	}

	[Test]
	public async Task Client_hello_frame_is_wellformed_json()
	{
		var frame = ResumeFrameParser.Hello();
		await Assert.That(frame).IsEqualTo("{\"type\":\"hello\"}");
	}


	[Test]
	public async Task Reads_resume_token_control_frame()
	{
		var ok = ResumeFrameParser.TryReadResumeToken("{\"type\":\"resumeToken\",\"token\":\"ABC123\"}", out var token);
		await Assert.That(ok).IsTrue();
		await Assert.That(token).IsEqualTo("ABC123");
	}

	[Test]
	public async Task Reads_seq_envelope_and_yields_inner_payload()
	{
		var ok = ResumeFrameParser.TryReadSeq("{\"type\":\"seq\",\"seq\":42,\"data\":\"look here\"}", out var seq, out var data);
		await Assert.That(ok).IsTrue();
		await Assert.That(seq).IsEqualTo(42L);
		await Assert.That(data).IsEqualTo("look here");
	}

	[Test]
	public async Task Passes_through_a_normal_oob_frame_as_neither()
	{
		const string oob = "{\"type\":\"html\",\"content\":\"<b>hi</b>\"}";
		await Assert.That(ResumeFrameParser.TryReadResumeToken(oob, out _)).IsFalse();
		await Assert.That(ResumeFrameParser.TryReadSeq(oob, out _, out _)).IsFalse();
	}

	[Test]
	public async Task Ignores_non_json_text()
	{
		await Assert.That(ResumeFrameParser.TryReadSeq("look north", out _, out _)).IsFalse();
		await Assert.That(ResumeFrameParser.TryReadResumeToken("plain text", out _)).IsFalse();
	}

	[Test]
	public async Task Recognises_reattached_ack_only_when_true()
	{
		await Assert.That(ResumeFrameParser.IsReattached("{\"type\":\"reattached\"}")).IsTrue();
		await Assert.That(ResumeFrameParser.IsReattached("{\"type\":\"bye\"}")).IsFalse();
		await Assert.That(ResumeFrameParser.IsReattached("{\"type\":\"seq\",\"seq\":1,\"data\":\"x\"}")).IsFalse();
		await Assert.That(ResumeFrameParser.IsReattached("{\"type\":\"markup\",\"data\":\"x\"}")).IsFalse();
		await Assert.That(ResumeFrameParser.IsReattached("look")).IsFalse();
	}

	[Test]
	public async Task Recognises_bye_only_when_true_and_never_confuses_other_frames()
	{
		await Assert.That(ResumeFrameParser.IsBye("{\"type\":\"bye\"}")).IsTrue();
		await Assert.That(ResumeFrameParser.IsBye("{\"type\":\"reattached\"}")).IsFalse();
		await Assert.That(ResumeFrameParser.IsBye("{\"type\":\"seq\",\"seq\":1,\"data\":\"x\"}")).IsFalse();
		await Assert.That(ResumeFrameParser.IsBye("{\"type\":\"markup\",\"data\":\"x\"}")).IsFalse();
		await Assert.That(ResumeFrameParser.IsBye("say goodbye")).IsFalse();
	}
}
