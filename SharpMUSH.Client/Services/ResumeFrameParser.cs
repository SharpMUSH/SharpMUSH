using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side inverse of the server's <c>SeqEnvelope</c>: recognises the resume-token control frame
/// and the <c>{"seq","data"}</c> output envelope so the terminal can track progress and replay on
/// reconnect. Pure and side-effect-free for easy testing.
/// </summary>
public static class ResumeFrameParser
{
	/// <summary>True if the frame is a <c>{"resumeToken":"..."}</c> control frame.</summary>
	public static bool TryReadResumeToken(string frame, out string? token)
	{
		token = null;
		if (!LooksLikeJson(frame)) return false;
		try
		{
			using var doc = JsonDocument.Parse(frame);
			if (doc.RootElement.TryGetProperty("resumeToken", out var el))
			{
				token = el.GetString();
				return token is not null;
			}
		}
		catch (JsonException) { }
		return false;
	}

	/// <summary>True if the frame is a <c>{"seq":n,"data":"..."}</c> envelope; yields seq + inner payload.</summary>
	public static bool TryReadSeq(string frame, out long seq, out string? data)
	{
		seq = 0;
		data = null;
		if (!LooksLikeJson(frame)) return false;
		try
		{
			using var doc = JsonDocument.Parse(frame);
			var root = doc.RootElement;
			if (root.TryGetProperty("seq", out var seqEl) && root.TryGetProperty("data", out var dataEl))
			{
				seq = seqEl.GetInt64();
				data = dataEl.GetString();
				return true;
			}
		}
		catch (JsonException) { }
		return false;
	}

	private static bool LooksLikeJson(string frame) => frame.Length > 0 && frame[0] == '{';
}
