using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side codec for the terminal control frames, the inverse of the server's <c>SeqEnvelope</c>.
/// Builds the outbound first frame (<c>{"hello":1}</c> / <c>{"resume","lastSeq"}</c>) and recognises the
/// inbound <c>{"reattached"}</c>, <c>{"resumeToken"}</c>, and <c>{"seq","data"}</c> frames. Pure and
/// side-effect-free for easy testing.
/// </summary>
public static class ResumeFrameParser
{
	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>The fresh-connect first frame: <c>{"hello":1}</c>.</summary>
	public static string Hello() => JsonSerializer.Serialize(new HelloFrame(1), Json);

	/// <summary>The reconnect first frame: <c>{"resume":"...","lastSeq":n}</c>.</summary>
	public static string Resume(string token, long lastSeq)
		=> JsonSerializer.Serialize(new ResumeFrame(token, lastSeq), Json);

	/// <summary>True if the frame is the <c>{"reattached":true}</c> rebind acknowledgement.</summary>
	public static bool IsReattached(string frame)
	{
		if (!LooksLikeJson(frame)) return false;
		try
		{
			using var doc = JsonDocument.Parse(frame);
			return doc.RootElement.TryGetProperty("reattached", out var el)
				&& el.ValueKind == JsonValueKind.True;
		}
		catch (JsonException)
		{
			return false;
		}
	}

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

			return false;
		}
		catch (JsonException)
		{
			return false;
		}
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

			return false;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool LooksLikeJson(string frame) => frame.Length > 0 && frame[0] == '{';

	private sealed record HelloFrame(int Hello);

	private sealed record ResumeFrame(string Resume, long LastSeq);
}
