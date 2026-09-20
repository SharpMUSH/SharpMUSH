using System.Text.Json;
using MarkupString;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// Out-of-band data for a WebSocket client, carried as a layer over the plain-text fallback every
/// other client sees. PennMUSH writes the same pair into the line as a <c>MARKUP_WS</c> region for
/// the data followed by a <c>MARKUP_WS_ALT</c> region for the fallback (<c>markup_websocket</c>,
/// <c>src/websock.c:546</c>); a layer says it in one span, which is what lets
/// <c>@emit [wshtml(&lt;b&gt;x&lt;/b&gt;,x)]</c> deliver both readings of the same emission.
/// </summary>
/// <remarks>
/// The <em>body</em> of the run is the fallback, never the data. A renderer that does not know this
/// kind therefore emits the fallback and nothing else — the layer becomes an
/// <see cref="UnknownMarkup"/>, which round-trips verbatim and renders as nothing — so the payload
/// can only ever be lost, never leaked in-band to a client that cannot read it. That is why no
/// emitter is registered for any format: passing the body through unchanged is already correct
/// everywhere but the portal, which reads the payload off the markup it is sent.
/// </remarks>
public sealed record WebSocketMarkup(string Channel, string Data) : IMarkup
{
	/// <summary>PennMUSH's <c>WEBSOCKET_CHANNEL_JSON</c>.</summary>
	public const string JsonChannel = "json";

	/// <summary>PennMUSH's <c>WEBSOCKET_CHANNEL_HTML</c>.</summary>
	public const string HtmlChannel = "html";
}

/// <summary>
/// Reads and writes <see cref="WebSocketMarkup"/> in the serializer's envelope, under kind
/// <c>"ws"</c>. Keys are <c>c</c> (channel) and <c>d</c> (data).
/// </summary>
public sealed class WebSocketMarkupCodec : IMarkupCodec
{
	public string Kind => "ws";

	public Type MarkupType => typeof(WebSocketMarkup);

	public void Write(Utf8JsonWriter writer, IMarkup markup)
	{
		if (markup is not WebSocketMarkup webSocket) return;
		writer.WriteString("c", webSocket.Channel);
		writer.WriteString("d", webSocket.Data);
	}

	public IMarkup Read(JsonElement element)
		=> new WebSocketMarkup(
			element.TryGetProperty("c", out var channel) ? channel.GetString() ?? string.Empty : string.Empty,
			element.TryGetProperty("d", out var data) ? data.GetString() ?? string.Empty : string.Empty);
}

/// <summary>Installs <see cref="WebSocketMarkupCodec"/> into a <see cref="MarkupRegistry"/>.</summary>
public static class WebSocketMarkupRegistration
{
	/// <summary>
	/// Returns a registry that serialises <see cref="WebSocketMarkup"/> under kind <c>"ws"</c>. A host
	/// that produces the markup needs this — serialising a layer with no codec throws — and a host
	/// that reads it back needs it only to see the payload; without it the fallback still renders.
	/// </summary>
	public static MarkupRegistry WithWebSocket(this MarkupRegistry registry)
		=> registry.With(new WebSocketMarkupCodec());
}
