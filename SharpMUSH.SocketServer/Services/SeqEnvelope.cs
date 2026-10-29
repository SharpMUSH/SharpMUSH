using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// Server-side codec for the terminal control frames exchanged with the client, all serialized via
/// <see cref="System.Text.Json"/> (camelCase) and sharing one flat discriminated envelope
/// <c>{"type":"&lt;kind&gt;",...}</c>: the sequenced output envelope <c>{"type":"seq","seq":n,"data":&lt;frame&gt;}</c>,
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

	/// <summary>Leaves the text's non-ASCII as UTF-8: the frame goes to a JSON parser, never into a document.</summary>
	private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	public static byte[] Wrap(long seq, string data)
		=> Wrap(seq, Encoding.UTF8.GetBytes(data));

	/// <summary>
	/// Wraps one output frame. A frame that is a JSON object (markup, OOB) is written in place as
	/// <c>data</c>, so the client parses it once and nothing in it is escaped a second time; plain text
	/// (a banner, a notice) is a string.
	/// </summary>
	public static byte[] Wrap(long seq, ReadOnlySpan<byte> utf8Data)
	{
		var buffer = new ArrayBufferWriter<byte>(utf8Data.Length + 48);
		using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
		{
			writer.WriteStartObject();
			writer.WriteString("type", "seq");
			writer.WriteNumber("seq", seq);
			writer.WritePropertyName("data");
			if (IsJsonObject(utf8Data)) writer.WriteRawValue(utf8Data, skipInputValidation: true);
			else writer.WriteStringValue(Encoding.UTF8.GetString(utf8Data));
			writer.WriteEndObject();
		}
		return buffer.WrittenSpan.ToArray();
	}

	private static bool IsJsonObject(ReadOnlySpan<byte> utf8)
	{
		if (utf8.IsEmpty || utf8[0] != (byte)'{') return false;
		try
		{
			var reader = new Utf8JsonReader(utf8);
			reader.Read();
			reader.Skip();
			return !reader.Read();
		}
		catch (JsonException)
		{
			return false;
		}
	}

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
	public static bool IsHello(string frame) => TryReadHello(frame, out _);

	/// <summary>
	/// True only if the frame is the structured <c>{"type":"hello"}</c> handshake, yielding its presence
	/// class ("play" for a real session, "portal" for a background query connection). Defaults to "play"
	/// when the <c>class</c> field is absent, so older clients and telnet keep counting as normal sessions.
	/// </summary>
	public static bool TryReadHello(string frame, out string presenceClass)
	{
		presenceClass = "play";
		try
		{
			var hello = JsonSerializer.Deserialize<HelloFrame>(frame, Json);
			if (hello?.Type != "hello") return false;
			presenceClass = string.IsNullOrEmpty(hello.Class) ? "play" : hello.Class;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static bool TryReadResume(string frame, out string token, out long lastSeq)
		=> TryReadResume(frame, out token, out lastSeq, out _);

	/// <summary>
	/// Whether a hello or resume first frame says the client logs in the moment the socket opens (<c>"signIn":true</c>),
	/// with a token or as a guest. Such a connection is never shown the connect screen.
	/// </summary>
	public static bool ReadsSignIn(string frame)
	{
		try
		{
			return JsonSerializer.Deserialize<HelloFrame>(frame, Json) is { Type: "hello" or "resume", SignIn: true };
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// Reads a <c>{"type":"resume",...}</c> first frame, also yielding its presence class (default "play"
	/// when absent) so a resume-to-dead re-registration still knows whether the connection is play or portal.
	/// </summary>
	public static bool TryReadResume(string frame, out string token, out long lastSeq, out string presenceClass)
	{
		token = string.Empty;
		lastSeq = 0;
		presenceClass = "play";
		try
		{
			var resume = JsonSerializer.Deserialize<ResumeFrame>(frame, Json);
			if (resume?.Type != "resume" || resume.Token is null) return false;
			token = resume.Token;
			lastSeq = resume.LastSeq;
			presenceClass = string.IsNullOrEmpty(resume.Class) ? "play" : resume.Class;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private sealed record HelloFrame(string? Type, string? Class, bool? SignIn = null);

	private sealed record SeqFrame(string Type, long? Seq);

	private sealed record ResumeFrame(string Type, string? Token, long LastSeq, string? Class, bool? SignIn = null);

	private sealed record ResumeTokenFrame(string Type, string Token);

	private sealed record ReattachedFrame(string Type);

	private sealed record ByeFrame(string Type);
}
