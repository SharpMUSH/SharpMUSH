using SharpMUSH.ConnectionServer.ProtocolHandlers;
using MarkupString.Mxp;
using System.Collections.Concurrent;
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
			OutputFormat.Mxp => ms.Render(MarkupFormat.Mxp, MxpWireFor(connection.Capabilities.MxpSupported)),
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
	/// One registry per set of answers, since a registry is immutable and connections that answered the
	/// same thing — which most clients of the same make do — can share one.
	/// </summary>
	private static readonly ConcurrentDictionary<string, MarkupRegistry> MxpWiresByAnswer = new(StringComparer.Ordinal);

	/// <summary>
	/// The registry for a connection whose client answered <paramref name="supported"/>: the elements it
	/// named, and nothing else. A sound it did not answer about writes nothing, and a frame it did not
	/// answer about leaves the text that would have gone in it where the player can read it.
	/// </summary>
	/// <param name="supported">
	/// The elements the client said it renders, space separated. Empty while the question is outstanding
	/// or when the client never answered. Null when it was never asked — a connection negotiated before
	/// this server asked — in which case every element is written, which is what such a client is owed.
	/// </param>
	private static MarkupRegistry MxpWireFor(string? supported)
	{
		if (supported is null) return MxpWire.Value;

		return MxpWiresByAnswer.GetOrAdd(supported, static answer =>
		{
			var elements = new HashSet<string>(
				answer.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

			return MarkupRegistry.Default.WithMxp(elements.Contains).WithMxpSecureLines();
		});
	}

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
