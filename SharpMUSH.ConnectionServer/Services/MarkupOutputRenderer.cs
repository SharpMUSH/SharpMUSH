using SharpMUSH.ConnectionServer.ProtocolHandlers;
using System.Text;
using System.Text.Json;
using SharpMUSH.ConnectionServer.Models;

namespace SharpMUSH.ConnectionServer.Services;

public sealed class MarkupOutputRenderer : IMarkupOutputRenderer
{
	/// <summary>Connection type value used by the WebSocket gateway when registering connections.</summary>
	public const string WebSocketConnectionType = "websocket";

	/// <summary>
	/// MXP open line prefix: ESC[0z — signals an MXP client that the line may contain standard
	/// open-mode MXP tags. Without it, MXP clients default to locked mode and ignore tags.
	/// </summary>
	private const string MxpOpenLinePrefix = ProtocolConstants.MxpLineOpen;

	public RenderedOutput Render(string markup, ConnectionServerService.ConnectionData connection) =>
		Render(markup, new RenderContext(connection.ConnectionType, connection.Capabilities, connection.Preferences));

	public RenderedOutput Render(string markup, RenderContext connection)
	{
		if (connection.ConnectionType == WebSocketConnectionType)
		{
			// Forward the markup untouched inside the out-of-band envelope; the browser renders it.
			var envelope = JsonSerializer.Serialize(new { type = "markup", data = markup });
			return new RenderedOutput(Encoding.UTF8.GetBytes(envelope), ApplyOutputTransform: false);
		}

		var ms = MarkupTextSerializer.Deserialize(markup);
		var text = connection.Capabilities.Format switch
		{
			OutputFormat.Pueblo => ms.Render(MarkupFormat.Pueblo),
			OutputFormat.Mxp => ApplyMxpLinePrefix(ms.Render(MarkupFormat.Mxp)),
			// The ANSI render for everything else, which maps an HtmlMarkup span — every exit name is
			// wrapped in <send> — to its ANSI equivalent, or to plain text when it has none. A client
			// that negotiated neither Pueblo nor MXP must never see a literal tag.
			_ => ms.Render(MarkupFormat.Ansi)
		};

		text = NormalizeLineEnding(text);
		return new RenderedOutput(Encoding.UTF8.GetBytes(text), ApplyOutputTransform: true);
	}

	/// <summary>
	/// Prepends the MXP open line prefix (ESC[0z) to each non-empty line so MXP clients interpret
	/// open-mode tags on every line.
	/// </summary>
	private static string ApplyMxpLinePrefix(string text)
	{
		var lines = text.Split('\n');
		return string.Join('\n', lines.Select(line =>
			line.Length == 0 || line == "\r" ? line : MxpOpenLinePrefix + line));
	}

	/// <summary>
	/// Normalizes line endings to \r\n and trims any trailing newline (mirrors the legacy
	/// NotifyService behavior that previously produced telnet bytes).
	/// </summary>
	private static string NormalizeLineEnding(string text)
	{
		text = text.Replace("\r\n", "\n");
		text = text.Replace("\n", "\r\n");
		return text.TrimEnd('\r', '\n');
	}
}
