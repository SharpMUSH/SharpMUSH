using System.Text;
using System.Text.Json;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Server-side codec for the terminal control frames exchanged with the client, all serialized via
/// <see cref="System.Text.Json"/> (camelCase) and sharing one flat discriminated envelope
/// <c>{"type":"&lt;kind&gt;",...}</c>: the sequenced output envelope <c>{"type":"seq","seq":n,"data":"..."}</c>,
/// the <c>{"type":"resumeToken","token":"..."}</c> handshake, the <c>{"type":"reattached"}</c> rebind ack,
/// the <c>{"type":"bye"}</c> logout notice, and the inbound <c>{"type":"hello"}</c> /
/// <c>{"type":"resume","token":"...","lastSeq":n}</c> requests.
/// </summary>
public static class SeqEnvelope
{
	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public static byte[] Wrap(long seq, string data)
		=> JsonSerializer.SerializeToUtf8Bytes(new SeqFrame("seq", seq, data), Json);

	public static byte[] Wrap(long seq, ReadOnlySpan<byte> utf8Data)
		=> Wrap(seq, Encoding.UTF8.GetString(utf8Data));

	/// <summary>Serializes the <c>{"type":"resumeToken","token":"..."}</c> control frame.</summary>
	public static byte[] ResumeToken(string token)
		=> JsonSerializer.SerializeToUtf8Bytes(new ResumeTokenFrame("resumeToken", token), Json);

	/// <summary>Serializes the <c>{"type":"reattached"}</c> rebind acknowledgement.</summary>
	public static byte[] Reattached()
		=> JsonSerializer.SerializeToUtf8Bytes(new ReattachedFrame("reattached"), Json);

	/// <summary>
	/// Serializes the <c>{"type":"bye"}</c> terminal-close frame. Sent immediately before the socket is
	/// closed on an engine-initiated disconnect (QUIT / ban / @boot) so the client knows the session
	/// ended deliberately and must NOT auto-reconnect — distinguishing it from a raw socket drop, which
	/// carries no bye and stays resumable within the grace window.
	/// </summary>
	public static byte[] Bye()
		=> JsonSerializer.SerializeToUtf8Bytes(new ByeFrame("bye"), Json);

	/// <summary>Reads the sequence of an output envelope, throwing if the frame is not one.</summary>
	public static long ReadSeq(byte[] frame)
	{
		var parsed = JsonSerializer.Deserialize<SeqFrame>(frame, Json);
		if (parsed?.Type != "seq" || parsed.Seq is null)
			throw new FormatException("Frame is not a sequence envelope (missing seq).");
		return parsed.Seq.Value;
	}

	/// <summary>Non-throwing variant of <see cref="ReadSeq"/>: false if the frame is not a seq envelope.</summary>
	public static bool TryReadSeq(byte[] frame, out long seq)
	{
		seq = 0;
		try
		{
			var parsed = JsonSerializer.Deserialize<SeqFrame>(frame, Json);
			if (parsed?.Type != "seq" || parsed.Seq is null) return false;
			seq = parsed.Seq.Value;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// True only if the frame is the structured <c>{"type":"hello"}</c> handshake. Matching on the
	/// discriminator (not a substring) keeps a real first command like <c>say "hello"</c> from being
	/// misclassified as the handshake and dropped.
	/// </summary>
	public static bool IsHello(string frame)
	{
		try
		{
			return JsonSerializer.Deserialize<TypedFrame>(frame, Json)?.Type == "hello";
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static bool TryReadResume(string frame, out string token, out long lastSeq)
	{
		token = string.Empty;
		lastSeq = 0;
		try
		{
			var resume = JsonSerializer.Deserialize<ResumeFrame>(frame, Json);
			if (resume?.Type != "resume" || resume.Token is null) return false;
			token = resume.Token;
			lastSeq = resume.LastSeq;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private sealed record TypedFrame(string? Type);

	private sealed record SeqFrame(string Type, long? Seq, string? Data);

	private sealed record ResumeFrame(string Type, string? Token, long LastSeq);

	private sealed record ResumeTokenFrame(string Type, string Token);

	private sealed record ReattachedFrame(string Type);

	private sealed record ByeFrame(string Type);
}
