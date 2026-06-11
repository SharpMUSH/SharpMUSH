using System.Text;
using System.Text.Json;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.Library.Definitions;
using MModule = MarkupString.MarkupStringModule;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// The result of rendering serialized markup for a specific connection.
/// </summary>
/// <param name="Data">The bytes to write to the connection.</param>
/// <param name="ApplyOutputTransform">
/// Whether the caller should still run the capability-based <see cref="IOutputTransformService"/>
/// over <see cref="Data"/> (true for terminal output; false for the WebSocket markup envelope,
/// which is JSON the browser renders itself and must not be ANSI/charset-transformed).
/// </param>
public readonly record struct RenderedOutput(byte[] Data, bool ApplyOutputTransform);

/// <summary>
/// Turns serialized markup (an <c>MString</c> as JSON, carried by
/// <see cref="SharpMUSH.Messaging.Messages.MarkupOutputMessage"/> /
/// <see cref="SharpMUSH.Messaging.Messages.MarkupPromptMessage"/>) into the wire form for a
/// connection. This is where the engine's markup model meets the transport:
/// <list type="bullet">
/// <item>WebSocket (portal) connections receive a <c>{ "type": "markup", "data": &lt;markup&gt; }</c>
/// envelope so the browser can render it with the MarkupString HTML renderer.</item>
/// <item>Terminal connections are rendered to ANSI / Pueblo / MXP per the connection's negotiated
/// <see cref="ProtocolCapabilities.Format"/>, with line endings normalized.</item>
/// </list>
/// </summary>
public interface IMarkupOutputRenderer
{
	RenderedOutput Render(string markup, ConnectionServerService.ConnectionData connection);
}

public sealed class MarkupOutputRenderer : IMarkupOutputRenderer
{
	/// <summary>Connection type value used by the WebSocket gateway when registering connections.</summary>
	public const string WebSocketConnectionType = "websocket";

	/// <summary>
	/// MXP open line prefix: ESC[0z — signals an MXP client that the line may contain standard
	/// open-mode MXP tags. Without it, MXP clients default to locked mode and ignore tags.
	/// </summary>
	private const string MxpOpenLinePrefix = ErrorMessages.Notifications.MxpLineOpen;

	public RenderedOutput Render(string markup, ConnectionServerService.ConnectionData connection)
	{
		if (connection.ConnectionType == WebSocketConnectionType)
		{
			// Forward the markup untouched inside the out-of-band envelope; the browser renders it.
			var envelope = JsonSerializer.Serialize(new { type = "markup", data = markup });
			return new RenderedOutput(Encoding.UTF8.GetBytes(envelope), ApplyOutputTransform: false);
		}

		var ms = MModule.deserialize(markup);
		var text = connection.Capabilities.Format switch
		{
			OutputFormat.Pueblo => ms.Render("pueblo"),
			OutputFormat.Mxp => ApplyMxpLinePrefix(ms.Render("mxp")),
			// Ansi (default): use the native ANSI representation, matching the legacy NotifyService path.
			_ => ms.ToString()
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
