using MarkupString;
using SharpMUSH.SocketServer.ProtocolHandlers;
using MarkupString.Ansi;
using MarkupString.Layout;
using MarkupString.Mxp;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.RenderingWorker.Services;

public sealed class MarkupOutputRenderer : IMarkupOutputRenderer
{
	/// <summary>Connection type value used by the WebSocket gateway when registering connections.</summary>
	public const string WebSocketConnectionType = "websocket";

	public ValueTask<RenderedOutput> RenderAsync(string markup, ConnectionServerService.ConnectionData connection,
		bool prompt = false, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(Render(markup, connection, prompt));
	}

	public RenderedOutput Render(string markup, ConnectionServerService.ConnectionData connection, bool prompt = false) =>
		Render(markup, new RenderContext(connection.ConnectionType, connection.Capabilities, connection.Preferences), prompt);

	public RenderedOutput Render(string markup, RenderContext connection, bool prompt = false)
	{
		if (connection.ConnectionType == WebSocketConnectionType)
		{
			// Forward the markup untouched inside the out-of-band envelope; the browser renders it.
			var envelope = JsonSerializer.Serialize(new { type = "markup", data = markup });
			return new RenderedOutput(Encoding.UTF8.GetBytes(envelope), ApplyOutputTransform: false);
		}

		var ms = Relayout(MarkupTextSerializer.Deserialize(markup), connection.Capabilities, connection.Preferences?.Theme);
		var depth = ColorDepthFor(connection.Capabilities, connection.Preferences);
		var text = connection.Capabilities.Format switch
		{
			OutputFormat.Pueblo => ms.Render(MarkupFormat.Pueblo, WireFor(depth)),
			OutputFormat.Mxp => ms.Render(MarkupFormat.Mxp, MxpWireFor(connection.Capabilities.MxpSupported, depth)),
			// The ANSI render for everything else, which maps a command link or a tagwrap() span to its
			// ANSI equivalent, or to plain text when it has none. A client that negotiated neither
			// Pueblo nor MXP must never see a literal tag.
			_ => ms.Render(MarkupFormat.Ansi, WireFor(depth))
		};

		text = NormalizeLineEnding(text);

		// A Pueblo client renders the stream as HTML, where the CRLF the telnet layer puts after each
		// message is whitespace: without a break of its own every message would run into the next.
		// PennMUSH ends a line the same way in HTML mode (queue_eol, src/notify.c). A prompt gets none —
		// what follows it is the player's own typing, on the same line.
		if (connection.Capabilities.Format == OutputFormat.Pueblo && !prompt
			&& !text.EndsWith(PuebloLineBreak, StringComparison.Ordinal))
		{
			text += PuebloLineBreak;
		}

		return new RenderedOutput(Encoding.UTF8.GetBytes(text), ApplyOutputTransform: true);
	}

	/// <summary>
	/// <paramref name="text"/> with each intact layout block (<c>box()</c>, <c>flex()</c>, ...) laid out
	/// for this client: an automatic-width block at the width it reported, box drawing as ASCII for a
	/// client without UTF-8, the content alone in reading order for a screen reader, and under the
	/// player's <c>@theme</c>, which sits over the game's look and under a layout's own theme. The browser
	/// lays blocks out itself, so this is for every other connection.
	/// </summary>
	private static MarkupText Relayout(MarkupText text, ProtocolCapabilities capabilities, string? theme)
	{
		if (text.Runs.IsDefaultOrEmpty) return text;

		var look = ReaderTheme(theme);
		var context = !capabilities.SupportsUtf8 || capabilities.ScreenReader || look is not null
			? new LayoutContext { AsciiOnly = !capabilities.SupportsUtf8, Linear = capabilities.ScreenReader, Theme = look ?? LayoutTheme.Default }
			: LayoutContext.Default;
		return BlockLayout.Relayout(text, capabilities.Width, context);
	}

	/// <summary>Each theme a player has set, as the layout theme it makes, or null for one that no longer reads.</summary>
	private static readonly ConcurrentDictionary<string, LayoutTheme?> ReaderThemes = new(StringComparer.Ordinal);

	/// <summary>The layout theme <paramref name="theme"/> makes, or null for none.</summary>
	private static LayoutTheme? ReaderTheme(string? theme)
	{
		if (string.IsNullOrWhiteSpace(theme)) return null;
		// A handful of themes are in use at once; past that, start again rather than grow without bound.
		if (ReaderThemes.Count > 256) ReaderThemes.Clear();
		return ReaderThemes.GetOrAdd(theme, static spec =>
			ThemePalette.TryParse(spec, out var palette, out _) ? palette!.ToLayoutTheme() : null);
	}

	/// <summary>
	/// The colour this connection is sent, from <see cref="TerminalCapabilityReader.ResolveColorStyle"/>, the
	/// calculation the engine also uses for <c>terminfo()</c> and <c>SOCKSET</c>, so what goes on the wire and
	/// what the player is told cannot drift apart.
	/// <para>
	/// A player flag and a negotiated terminal capability are both claims that the client can display
	/// something, so they are unioned: whichever says yes wins, and the deepest rung either of them reaches is
	/// the one used. Neither can veto the other, because a flag that is <i>not</i> set is indistinguishable from
	/// one nobody has thought about. Refusing colour is <c>SOCKSET colorstyle</c>'s job, and a pin from it
	/// overrides everything, including the screen-reader default.
	/// </para>
	/// </summary>
	private static AnsiColorDepth ColorDepthFor(ProtocolCapabilities capabilities, PlayerOutputPreferences? preferences) =>
		TerminalCapabilityReader.ResolveColorStyle(
			capabilities.ColorStylePin,
			new TerminalCapabilities(
				Ansi: capabilities.SupportsAnsi,
				Xterm256: capabilities.SupportsXterm256,
				Truecolor: capabilities.SupportsTruecolor,
				Utf8: capabilities.SupportsUtf8,
				ScreenReader: capabilities.ScreenReader),
			preferences is null
				? null
				: new PlayerColorFlags(preferences.AnsiEnabled, preferences.ColorEnabled,
					preferences.Xterm256Enabled, preferences.TruecolorEnabled)) switch
		{
			ColorStyles.Plain => AnsiColorDepth.None,
			ColorStyles.Hilite => AnsiColorDepth.Attributes,
			ColorStyles.Xterm256 => AnsiColorDepth.Xterm256,
			ColorStyles.Truecolor => AnsiColorDepth.TrueColor,
			_ => AnsiColorDepth.Standard
		};

	/// <summary>
	/// The registry that renders ANSI and Pueblo at <paramref name="depth"/>. Telnet clients do not read OSC 8,
	/// so a link is its text. Built on first use because <see cref="MarkupRegistry.Default"/> is installed at
	/// startup, after this type loads, and shared, since a registry is immutable.
	/// </summary>
	private static MarkupRegistry WireFor(AnsiColorDepth depth) =>
		WiresByDepth.GetOrAdd(depth, static depth => MarkupRegistry.Default.WithAnsiOutput(depth, hyperlinks: false));

	private static readonly ConcurrentDictionary<AnsiColorDepth, MarkupRegistry> WiresByDepth = new();

	/// <summary>The break a Pueblo client reads as the end of a line.</summary>
	private const string PuebloLineBreak = "<BR>";

	/// <summary>
	/// One registry per set of answers and colour depth, since a registry is immutable and connections that
	/// answered the same thing — which most clients of the same make do — can share one.
	/// </summary>
	private static readonly ConcurrentDictionary<(string? Supported, AnsiColorDepth Depth), MarkupRegistry> MxpWires = new();

	/// <summary>
	/// The registry for an MXP connection whose client answered <paramref name="supported"/>: the elements it
	/// named and nothing else, every line opened in secure mode (which an MXP client needs before it reads the
	/// tags on that line), and colour at <paramref name="depth"/>. A sound it did not answer about writes
	/// nothing, and a frame it did not answer about leaves the text that would have gone in it where the player
	/// can read it.
	/// </summary>
	/// <param name="supported">
	/// The elements the client said it renders, space separated. Empty while the question is outstanding
	/// or when the client never answered. Null when it was never asked — a connection negotiated before
	/// this server asked — in which case every element is written, which is what such a client is owed.
	/// </param>
	/// <param name="depth">The colour the connection is sent.</param>
	private static MarkupRegistry MxpWireFor(string? supported, AnsiColorDepth depth) =>
		MxpWires.GetOrAdd((supported, depth), static key =>
		{
			var registry = key.Supported is null
				? MarkupRegistry.Default
				: MarkupRegistry.Default.WithMxp(new HashSet<string>(
					key.Supported.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase).Contains);

			return registry.WithMxpSecureLines().WithAnsiOutput(key.Depth, hyperlinks: false);
		});

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
