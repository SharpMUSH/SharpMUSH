using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The frames a WebSocket client is sent for markup: <c>{ "type": "markup", "data": &lt;markup&gt; }</c>, the
/// serialized <c>MString</c> written in place as a JSON object, never as a string holding JSON, so the
/// browser parses it once and nothing in it is escaped twice. A prompt is the same frame marked
/// <c>"prompt": true</c>, with the <c>@input</c> session it belongs to when it has one, so a client that
/// does not know the mark still shows it as a line. The end of a prompt is
/// <c>{ "type": "prompt", "clear": true, "session": … }</c>.
/// </summary>
public static class WebSocketMarkupFrames
{
	private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	/// <summary>A line of output. <paramref name="markup"/> is the serializer's JSON, or empty for nothing.</summary>
	public static byte[] Line(string markup) => Markup(markup, prompt: false, session: null);

	public static byte[] Prompt(string markup, string? inputSession) => Markup(markup, prompt: true, inputSession);

	public static byte[] Clear(string? inputSession) => Write(writer =>
	{
		writer.WriteString("type", "prompt");
		writer.WriteBoolean("clear", true);
		if (inputSession is not null) writer.WriteString("session", inputSession);
	});

	private static byte[] Markup(string markup, bool prompt, string? session) => Write(writer =>
	{
		writer.WriteString("type", "markup");
		writer.WritePropertyName("data");
		// The serializer writes {} for empty text, and an empty string is how the engine sends it.
		writer.WriteRawValue(markup.Length == 0 ? "{}" : markup);
		if (prompt) writer.WriteBoolean("prompt", true);
		if (session is not null) writer.WriteString("session", session);
	});

	private static byte[] Write(Action<Utf8JsonWriter> properties)
	{
		var buffer = new ArrayBufferWriter<byte>();
		using (var writer = new Utf8JsonWriter(buffer, Options))
		{
			writer.WriteStartObject();
			properties(writer);
			writer.WriteEndObject();
		}
		return buffer.WrittenSpan.ToArray();
	}
}
