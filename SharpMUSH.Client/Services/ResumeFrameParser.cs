using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side codec for the terminal control frames, the inverse of the server's <c>SeqEnvelope</c>.
/// Every frame shares one flat discriminated envelope <c>{"type":"&lt;kind&gt;",...}</c>. Builds the
/// outbound first frame (<c>{"type":"hello"}</c> / <c>{"type":"resume","token","lastSeq"}</c>) and
/// recognises the inbound <c>{"type":"reattached"}</c>, <c>{"type":"bye"}</c>,
/// <c>{"type":"resumeToken","token"}</c>, and <c>{"type":"seq","seq","data"}</c> frames (<see cref="Read"/>). Pure and
/// side-effect-free for easy testing.
/// </summary>
public static class ResumeFrameParser
{
	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>
	/// The fresh-connect first frame: <c>{"type":"hello","class":"&lt;presenceClass&gt;"}</c>. The presence
	/// class ("play" for a real interactive session, "portal" for a background query connection) rides on
	/// the first frame so the server knows it at registration time; it survives reconnects because a
	/// resume-to-dead re-registers fresh off its own first frame.
	/// </summary>
	/// <param name="signIn">The client logs in with a token or as a guest the moment the socket opens, so the
	/// server leaves out the connect screen it would otherwise draw for the login prompt.</param>
	public static string Hello(string presenceClass = "play", bool signIn = false)
		=> JsonSerializer.Serialize(new HelloFrame("hello", presenceClass, signIn ? true : null), Json);

	/// <summary>The reconnect first frame: <c>{"type":"resume","token":"...","lastSeq":n,"class":"..."}</c>.</summary>
	public static string Resume(string token, long lastSeq, string presenceClass = "play", bool signIn = false)
		=> JsonSerializer.Serialize(new ResumeFrame("resume", token, lastSeq, presenceClass, signIn ? true : null), Json);

	/// <summary>True if the frame is the <c>{"type":"reattached"}</c> rebind acknowledgement.</summary>
	public static bool IsReattached(string frame) => Read(frame) is ReattachedFrame;

	/// <summary>
	/// True if the frame is the <c>{"type":"bye"}</c> engine-initiated-logout notice — the server sent it
	/// just before closing an intentional disconnect (QUIT / ban / @boot), so the client must not
	/// auto-reconnect. Absent on a raw socket drop, which stays resumable.
	/// </summary>
	public static bool IsBye(string frame) => Read(frame) is ByeFrame;

	/// <summary>True if the frame is a <c>{"type":"resumeToken","token":"..."}</c> control frame.</summary>
	public static bool TryReadResumeToken(string frame, out string? token)
	{
		token = Read(frame) is ResumeTokenFrame resume ? resume.Token : null;
		return token is not null;
	}

	/// <summary>True if the frame is a <c>{"type":"seq","seq":n,"data":...}</c> envelope; yields seq + inner payload.</summary>
	public static bool TryReadSeq(string frame, out long seq, out string? data)
	{
		var read = Read(frame);
		(seq, data) = read is SequencedFrame sequenced ? (sequenced.Seq, sequenced.Data) : (0, null);
		return read is SequencedFrame;
	}

	/// <summary>
	/// Reads what a frame from the server is, in one parse: a frame of output can be large, and every
	/// control frame is checked for before it is shown. The payload of a sequence envelope is the frame it
	/// carries, as text: a JSON object (markup, OOB) as written, or the plain text of a banner.
	/// </summary>
	public static ServerFrame Read(string frame)
	{
		if (!LooksLikeJson(frame)) return new OtherFrame();
		try
		{
			using var doc = JsonDocument.Parse(frame);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("type", out var type)
				|| type.ValueKind != JsonValueKind.String)
				return new OtherFrame();
			return type.GetString() switch
			{
				"bye" => new ByeFrame(),
				"reattached" => new ReattachedFrame(),
				"resumeToken" when root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String
					=> new ResumeTokenFrame(token.GetString()!),
				"seq" when root.TryGetProperty("seq", out var seq) && seq.ValueKind == JsonValueKind.Number
					&& root.TryGetProperty("data", out var data) && Payload(data) is { } payload
					=> new SequencedFrame(seq.GetInt64(), payload),
				_ => new OtherFrame()
			};
		}
		catch (JsonException)
		{
			return new OtherFrame();
		}
	}

	private static string? Payload(JsonElement data) => data.ValueKind switch
	{
		JsonValueKind.String => data.GetString(),
		JsonValueKind.Object => data.GetRawText(),
		_ => null
	};

	private static bool LooksLikeJson(string frame) => frame.Length > 0 && frame[0] == '{';

	private sealed record HelloFrame(string Type, string Class,
		[property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? SignIn);

	private sealed record ResumeFrame(string Type, string Token, long LastSeq, string Class,
		[property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? SignIn);
}

/// <summary>A frame from the server, as <see cref="ResumeFrameParser.Read"/> found it.</summary>
public union ServerFrame(ByeFrame, ReattachedFrame, ResumeTokenFrame, SequencedFrame, OtherFrame);

/// <summary><c>{"type":"bye"}</c>: the server ended the session; do not reconnect.</summary>
public readonly record struct ByeFrame;

/// <summary><c>{"type":"reattached"}</c>: the resume was accepted.</summary>
public readonly record struct ReattachedFrame;

/// <summary><c>{"type":"resumeToken","token":"..."}</c>: the token a reconnect resumes with.</summary>
public readonly record struct ResumeTokenFrame(string Token);

/// <summary><c>{"type":"seq","seq":n,"data":...}</c>: a frame of output and its place in the session.</summary>
public readonly record struct SequencedFrame(long Seq, string Data);

/// <summary>Anything else: a frame that is not one of the above, shown as it is.</summary>
public readonly record struct OtherFrame;
