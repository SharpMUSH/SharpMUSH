using System.Text;
using System.Text.Json;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Server-side codec for the terminal control frames exchanged with the client, all serialized via
/// <see cref="System.Text.Json"/> (camelCase): the sequenced output envelope <c>{"seq":n,"data":"..."}</c>,
/// the <c>{"resumeToken":"..."}</c> handshake, the <c>{"reattached":true}</c> rebind ack, and the inbound
/// <c>{"resume":"...","lastSeq":n}</c> request.
/// </summary>
public static class SeqEnvelope
{
	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public static byte[] Wrap(long seq, string data)
		=> JsonSerializer.SerializeToUtf8Bytes(new SeqFrame(seq, data), Json);

	public static byte[] Wrap(long seq, ReadOnlySpan<byte> utf8Data)
		=> Wrap(seq, Encoding.UTF8.GetString(utf8Data));

	/// <summary>Serializes the <c>{"resumeToken":"..."}</c> control frame.</summary>
	public static byte[] ResumeToken(string token)
		=> JsonSerializer.SerializeToUtf8Bytes(new ResumeTokenFrame(token), Json);

	/// <summary>Serializes the <c>{"reattached":true}</c> rebind acknowledgement.</summary>
	public static byte[] Reattached()
		=> JsonSerializer.SerializeToUtf8Bytes(new ReattachedFrame(true), Json);

	/// <summary>Reads the sequence of an output envelope, throwing if the frame is not one.</summary>
	public static long ReadSeq(byte[] frame)
		=> JsonSerializer.Deserialize<SeqFrame>(frame, Json)?.Seq
		   ?? throw new FormatException("Frame is not a sequence envelope (missing seq).");

	/// <summary>Non-throwing variant of <see cref="ReadSeq"/>: false if the frame is not a seq envelope.</summary>
	public static bool TryReadSeq(byte[] frame, out long seq)
	{
		seq = 0;
		try
		{
			var parsed = JsonSerializer.Deserialize<SeqFrame>(frame, Json);
			if (parsed?.Seq is null) return false;
			seq = parsed.Seq.Value;
			return true;
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
			if (resume?.Resume is null) return false;
			token = resume.Resume;
			lastSeq = resume.LastSeq;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private sealed record SeqFrame(long? Seq, string Data);

	private sealed record ResumeFrame(string? Resume, long LastSeq);

	private sealed record ResumeTokenFrame(string ResumeToken);

	private sealed record ReattachedFrame(bool Reattached);
}
