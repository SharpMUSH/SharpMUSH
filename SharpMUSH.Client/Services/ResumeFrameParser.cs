using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side codec for the terminal control frames, the inverse of the server's <c>SeqEnvelope</c>.
/// Every frame shares one flat discriminated envelope <c>{"type":"&lt;kind&gt;",...}</c>. Builds the
/// outbound first frame (<c>{"type":"hello"}</c> / <c>{"type":"resume","token","lastSeq"}</c>) and
/// recognises the inbound <c>{"type":"reattached"}</c>, <c>{"type":"bye"}</c>,
/// <c>{"type":"resumeToken","token"}</c>, and <c>{"type":"seq","seq","data"}</c> frames. Pure and
/// side-effect-free for easy testing.
/// </summary>
public static class ResumeFrameParser
{
	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>The fresh-connect first frame: <c>{"type":"hello"}</c>.</summary>
	public static string Hello() => JsonSerializer.Serialize(new HelloFrame("hello"), Json);

	/// <summary>The reconnect first frame: <c>{"type":"resume","token":"...","lastSeq":n}</c>.</summary>
	public static string Resume(string token, long lastSeq)
		=> JsonSerializer.Serialize(new ResumeFrame("resume", token, lastSeq), Json);

	/// <summary>True if the frame is the <c>{"type":"reattached"}</c> rebind acknowledgement.</summary>
	public static bool IsReattached(string frame) => TypeEquals(frame, "reattached");

	/// <summary>
	/// True if the frame is the <c>{"type":"bye"}</c> engine-initiated-logout notice — the server sent it
	/// just before closing an intentional disconnect (QUIT / ban / @boot), so the client must not
	/// auto-reconnect. Absent on a raw socket drop, which stays resumable.
	/// </summary>
	public static bool IsBye(string frame) => TypeEquals(frame, "bye");

	/// <summary>True if the frame is a <c>{"type":"resumeToken","token":"..."}</c> control frame.</summary>
	public static bool TryReadResumeToken(string frame, out string? token)
	{
		token = null;
		if (!LooksLikeJson(frame)) return false;
		try
		{
			using var doc = JsonDocument.Parse(frame);
			var root = doc.RootElement;
			if (!IsType(root, "resumeToken") || !root.TryGetProperty("token", out var el))
				return false;
			token = el.GetString();
			return token is not null;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>True if the frame is a <c>{"type":"seq","seq":n,"data":"..."}</c> envelope; yields seq + inner payload.</summary>
	public static bool TryReadSeq(string frame, out long seq, out string? data)
	{
		seq = 0;
		data = null;
		if (!LooksLikeJson(frame)) return false;
		try
		{
			using var doc = JsonDocument.Parse(frame);
			var root = doc.RootElement;
			if (!IsType(root, "seq")
				|| !root.TryGetProperty("seq", out var seqEl)
				|| !root.TryGetProperty("data", out var dataEl))
				return false;
			seq = seqEl.GetInt64();
			data = dataEl.GetString();
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool TypeEquals(string frame, string kind)
	{
		if (!LooksLikeJson(frame)) return false;
		try
		{
			using var doc = JsonDocument.Parse(frame);
			return IsType(doc.RootElement, kind);
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool IsType(JsonElement root, string kind) =>
		root.ValueKind == JsonValueKind.Object
		&& root.TryGetProperty("type", out var el)
		&& el.ValueKind == JsonValueKind.String
		&& el.GetString() == kind;

	private static bool LooksLikeJson(string frame) => frame.Length > 0 && frame[0] == '{';

	private sealed record HelloFrame(string Type);

	private sealed record ResumeFrame(string Type, string Token, long LastSeq);
}
