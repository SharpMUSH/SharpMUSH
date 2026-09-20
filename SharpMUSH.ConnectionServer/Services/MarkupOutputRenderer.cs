using SharpMUSH.ConnectionServer.ProtocolHandlers;
using System.Text;
using System.Text.Json;
using SharpMUSH.ConnectionServer.Models;

namespace SharpMUSH.ConnectionServer.Services;

public sealed class MarkupOutputRenderer : IMarkupOutputRenderer
{
	/// <summary>Connection type value used by the WebSocket gateway when registering connections.</summary>
	public const string WebSocketConnectionType = "websocket";

	public ValueTask<RenderedOutput> RenderAsync(string markup, ConnectionServerService.ConnectionData connection,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(Render(markup, connection));
	}

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
			OutputFormat.Mxp => ms.Render(MarkupFormat.Mxp, MxpWire.Value),
			// The ANSI render for everything else, which maps a command link or a tagwrap() span to its
			// ANSI equivalent, or to plain text when it has none. A client that negotiated neither
			// Pueblo nor MXP must never see a literal tag.
			_ => ms.Render(MarkupFormat.Ansi)
		};

		text = NormalizeLineEnding(text);
		return new RenderedOutput(Encoding.UTF8.GetBytes(text), ApplyOutputTransform: true);
	}

	/// <summary>
	/// The registry that renders for an MXP connection: the one the host installed, plus the framer that
	/// opens each line in secure mode, which an MXP client needs before it reads the tags on that line.
	/// Lazy because <see cref="MarkupRegistry.Default"/> is installed at startup, after this type loads.
	/// </summary>
	private static readonly Lazy<MarkupRegistry> MxpWire = new(() => MarkupRegistry.Default.WithMxpSecureLines());

	/// <summary>
	/// Normalizes line endings to \r\n and trims any trailing newline (mirrors the legacy
	/// NotifyService behavior that previously produced telnet bytes).
	/// </summary>
	private static string NormalizeLineEnding(string text)
	{
		var trimmed = text.TrimEnd('\r', '\n');

		if (!trimmed.Contains('\n'))
		{
			return trimmed;
		}

		// Each line gives up the \r a CRLF already left on it, so the join never doubles the pair. A \r
		// that precedes anything else is ordinary text.
		return string.Join("\r\n", trimmed.Split('\n').Select(line => line.EndsWith('\r') ? line[..^1] : line));
	}
}
