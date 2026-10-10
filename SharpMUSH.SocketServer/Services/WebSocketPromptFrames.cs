using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The frames a WebSocket client is sent for a prompt and for its end. A prompt is the ordinary markup
/// envelope marked <c>"prompt": true</c>, with the <c>@input</c> session it belongs to when it has one,
/// so a client that does not know the mark still shows it as a line. The end is
/// <c>{ "type": "prompt", "clear": true, "session": … }</c>.
/// </summary>
public static class WebSocketPromptFrames
{
	private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	public static byte[] Prompt(string markup, string? inputSession)
		=> JsonSerializer.SerializeToUtf8Bytes(new PromptFrame("markup", markup, true, inputSession), Options);

	public static byte[] Clear(string? inputSession)
		=> JsonSerializer.SerializeToUtf8Bytes(new ClearFrame("prompt", true, inputSession), Options);

	private sealed record PromptFrame(
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("data")] string Data,
		[property: JsonPropertyName("prompt")] bool Prompt,
		[property: JsonPropertyName("session")] string? Session);

	private sealed record ClearFrame(
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("clear")] bool Clear,
		[property: JsonPropertyName("session")] string? Session);
}
