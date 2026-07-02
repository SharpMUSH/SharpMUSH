using System.Text;
using System.Text.Json;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Wraps a terminal output frame with a monotonic per-handle sequence number so the client can
/// acknowledge progress and request replay after a fresh reconnect: <c>{"seq":n,"data":"..."}</c>.
/// Only used when sequenced output is enabled (WebTransport feature flag); legacy output is raw.
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

	public static long ReadSeq(byte[] frame)
		=> JsonSerializer.Deserialize<SeqFrame>(frame, Json)?.Seq
		   ?? throw new FormatException("Frame is not a sequence envelope (missing seq).");

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
}
