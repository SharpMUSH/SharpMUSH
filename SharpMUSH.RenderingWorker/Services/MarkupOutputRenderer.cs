using MarkupString;
using SharpMUSH.SocketServer.ProtocolHandlers;
using MarkupString.Ansi;
using MarkupString.Layout;
using MarkupString.Mxp;
using SharpMUSH.Library.Markup;
using SharpMUSH.Configuration;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.RenderingWorker.Services;

public sealed class MarkupOutputRenderer(TerminalPictureStore? pictureStore, ConnectionPictures? connectionPictures)
	: IMarkupOutputRenderer
{
	/// <summary>Connection type value used by the WebSocket gateway when registering connections.</summary>
	public const string WebSocketConnectionType = "websocket";

	/// <summary>
	/// How long a render waits for a picture it does not hold yet before sending the text art in its place.
	/// The connection's later output waits behind it, so this is short; the picture is drawn next time.
	/// </summary>
	public static readonly TimeSpan PictureWait = TimeSpan.FromMilliseconds(1500);

	/// <summary>A renderer that draws no pictures: each is its text art.</summary>
	public MarkupOutputRenderer() : this(null, null)
	{
	}

	public ValueTask<RenderedOutput> RenderAsync(string markup, ConnectionServerService.ConnectionData connection,
		bool prompt = false, CancellationToken ct = default) =>
		RenderAsync(markup, ContextOf(connection), prompt, ct);

	/// <summary>
	/// <paramref name="markup"/> for <paramref name="connection"/>, waiting up to <see cref="PictureWait"/> for
	/// any picture it would draw that is still being fetched.
	/// </summary>
	public async ValueTask<RenderedOutput> RenderAsync(string markup, RenderContext connection, bool prompt = false,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var pictures = PicturesFor(connection);
		var rendered = Render(markup, connection, prompt, pictures);

		if (pictures is { Pending.Count: > 0 })
		{
			try
			{
				await Task.WhenAll(pictures.Pending).WaitAsync(PictureWait, ct);
			}
			catch (TimeoutException)
			{
				// Sent as it is; the picture is drawn the next time it is shown.
			}

			if (pictures.Pending.Any(fetch => fetch.IsCompleted))
			{
				pictures = PicturesFor(connection)!;
				rendered = Render(markup, connection, prompt, pictures);
			}
		}

		pictures?.Commit();
		return rendered;
	}

	public RenderedOutput Render(string markup, ConnectionServerService.ConnectionData connection, bool prompt = false) =>
		Render(markup, ContextOf(connection), prompt);

	/// <summary><paramref name="markup"/> for <paramref name="connection"/>, with only the pictures already held.</summary>
	public RenderedOutput Render(string markup, RenderContext connection, bool prompt = false)
	{
		var pictures = PicturesFor(connection);
		var rendered = Render(markup, connection, prompt, pictures);
		pictures?.Commit();
		return rendered;
	}

	private static RenderContext ContextOf(ConnectionServerService.ConnectionData connection) =>
		new(connection.ConnectionType, connection.Capabilities, connection.Preferences, connection.Handle, connection.SessionId);

	/// <summary>
	/// Where this render finds pictures: for a terminal that draws them, the shared store and what this
	/// connection's terminal already holds; for an MXP client that draws <c>&lt;IMAGE&gt;</c>, the store alone,
	/// for the size of each picture, so a figure keeps cells of the picture's shape for the client to draw it
	/// in; otherwise none, and every picture is its text art.
	/// </summary>
	private RenderPictureSource? PicturesFor(RenderContext connection)
	{
		if (pictureStore is null) return null;

		// The MXP client fetches the picture itself; nothing is transmitted, so nothing is remembered as held.
		if (connection.Capabilities.Format == OutputFormat.Mxp && MxpDrawsImages(connection.Capabilities.MxpSupported))
			return new RenderPictureSource(pictureStore, new ConnectionPictures.Sent());

		if (connectionPictures is null || connection.Capabilities.Format != OutputFormat.Ansi
			|| (connection.Capabilities.Features & TerminalFeatures.Pictures) == 0)
			return null;

		return new RenderPictureSource(pictureStore,
			connectionPictures.For($"{connection.Handle}:{connection.SessionId}",
				connection.Capabilities.Features.HasFlag(TerminalFeatures.MovingPictures)));
	}

	/// <summary>
	/// Whether an MXP client renders <c>&lt;IMAGE&gt;</c>: it said so, or has not been asked yet, which the MXP
	/// render already treats as every element (<see cref="MxpWireFor"/>).
	/// </summary>
	private static bool MxpDrawsImages(string? supported) =>
		supported is null || supported.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("IMAGE", StringComparer.OrdinalIgnoreCase);

	private static RenderedOutput Render(string markup, RenderContext connection, bool prompt, RenderPictureSource? pictures)
	{
		if (connection.ConnectionType == WebSocketConnectionType)
		{
			// Forward the markup untouched inside the out-of-band envelope; the browser renders it.
			var envelope = JsonSerializer.Serialize(new { type = "markup", data = markup });
			return new RenderedOutput(Encoding.UTF8.GetBytes(envelope), ApplyOutputTransform: false);
		}

		var depth = ColorDepthFor(connection.Capabilities, connection.Preferences);
		var ansi = AnsiOptionsFor(connection.Capabilities, depth, pictures);
		// A notice()'s lead without its brackets for a screen reader, and without its tags for everyone.
		var cells = ansi.Pictures is null ? null : PictureCellsFor(ansi);
		var fetchesItself = connection.Capabilities.Format is OutputFormat.Mxp or OutputFormat.Pueblo;
		if (fetchesItself && cells is not null)
		{
			// No cells kept for a picture the client cannot fetch, so its figure keeps the art.
			var measured = cells;
			cells = (image, columns) => ClientFetchedPictures.Resolve(image.Source, connection.Website) is not null
				? measured(image, columns)
				: null;
		}
		var fold = FoldFor(connection.Capabilities, connection.AsciiTranslations);
		var ms = Relayout(NoticeMarkup.ForTelnet(MarkupTextSerializer.Deserialize(markup), connection.Capabilities.ScreenReader),
			connection.Capabilities, connection.Preferences?.Theme, cells, fold);
		// The blocks were folded as they were laid out; this is the text around them.
		if (fold is not null) ms = fold.Fold(ms);
		if (fetchesItself) ms = ClientFetchedPictures.Fetchable(ms, connection.Website);
		var text = connection.Capabilities.Format switch
		{
			OutputFormat.Pueblo => ms.Render(MarkupFormat.Pueblo, WireFor(depth)),
			OutputFormat.Mxp => ms.Render(MarkupFormat.Mxp, MxpWireFor(connection.Capabilities.MxpSupported, depth)),
			// The ANSI render for everything else, which maps a command link or a tagwrap() span to its
			// ANSI equivalent, or to plain text when it has none. A client that negotiated neither
			// Pueblo nor MXP must never see a literal tag.
			_ => ms.Render(MarkupFormat.Ansi, AnsiWireFor(ansi))
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
	/// For a connection not written in UTF-8, the fold that replaces each character it cannot show: the game's
	/// <c>ascii_translations</c> first, then the built-in stand-ins, keeping what Latin-1 has for a client that
	/// reads it. Null for a connection written in UTF-8.
	/// </summary>
	public static AsciiFold? FoldFor(ProtocolCapabilities capabilities, string? translations)
	{
		if (capabilities.Utf8) return null;
		var latin1 = capabilities.OutputCharset == TerminalCharsets.Latin1;
		if (string.IsNullOrWhiteSpace(translations)) return latin1 ? Latin1Fold : AsciiFold.Default;

		// The game changes them rarely and every connection shares them; past a handful, start again.
		if (Folds.Count > 16) Folds.Clear();
		return Folds.GetOrAdd((translations, latin1), static key =>
		{
			// The engine refuses a value that does not read, so this is a value from before that check: the
			// built-in stand-ins alone, rather than none at all.
			if (!AsciiTranslations.TryParse(key.Translations, out var pairs, out _)) pairs = [];
			try
			{
				return new AsciiFold(pairs, key.Latin1);
			}
			catch (ArgumentException)
			{
				return new AsciiFold(latin1: key.Latin1);
			}
		});
	}

	private static readonly AsciiFold Latin1Fold = new(latin1: true);

	private static readonly ConcurrentDictionary<(string Translations, bool Latin1), AsciiFold> Folds = new();

	/// <summary>
	/// <paramref name="text"/> with each intact layout block (<c>box()</c>, <c>flex()</c>, ...) laid out
	/// for this client: an automatic-width block at the width it reported, box drawing as ASCII and its text
	/// folded (<paramref name="fold"/>) for a client without UTF-8, the content alone in reading order for a
	/// screen reader, and under the player's <c>@theme</c>, which sits over the game's look and under a
	/// layout's own theme. The browser lays blocks out itself, so this is for every other connection.
	/// </summary>
	private static MarkupText Relayout(MarkupText text, ProtocolCapabilities capabilities, string? theme,
		Func<ImageMarkup, int, PictureCells?>? pictures, AsciiFold? fold)
	{
		if (text.Runs.IsDefaultOrEmpty) return text;

		var look = ReaderTheme(theme);
		var context = fold is not null || capabilities.ScreenReader || look is not null || pictures is not null
			? new LayoutContext
			{
				AsciiOnly = fold is not null,
				Fold = fold,
				Linear = capabilities.ScreenReader,
				Theme = look ?? LayoutTheme.Default,
				Pictures = pictures
			}
			: LayoutContext.Default;
		return BlockLayout.Relayout(text, capabilities.Width, context);
	}

	/// <summary>The most cells a picture is drawn over, each way, however wide the space it is given.</summary>
	public const int MaxPictureColumns = 60;

	public const int MaxPictureRows = 30;

	/// <summary>
	/// The cells a figure's picture takes for a client that draws it: none for a picture not held (the
	/// figure keeps its text art), otherwise as wide as it was asked for, but no wider than the picture is
	/// at one pixel a pixel or than <see cref="MaxPictureColumns"/>, and short enough to keep within
	/// <see cref="MaxPictureRows"/> at its own shape.
	/// </summary>
	private static Func<ImageMarkup, int, PictureCells?> PictureCellsFor(AnsiOutputOptions options) => (image, columns) =>
	{
		if (columns <= 0 || options.Pictures is not { } source || !source.TryGetPicture(image, out var picture)) return null;

		var natural = Math.Max(1, (picture.Width + options.CellWidth - 1) / options.CellWidth);
		var cells = PictureCells.Fit(picture.Width, picture.Height, Math.Min(Math.Min(columns, natural), MaxPictureColumns),
			options.CellWidth, options.CellHeight);
		if (cells.Rows <= MaxPictureRows) return cells;

		var narrower = Math.Max(1, cells.Columns * MaxPictureRows / cells.Rows);
		return PictureCells.Fit(picture.Width, picture.Height, narrower, options.CellWidth, options.CellHeight);
	};

	/// <summary>
	/// What an ANSI client is sent: colour at <paramref name="depth"/>, the terminal features it has, and the
	/// pictures <paramref name="pictures"/> holds when it draws them.
	/// </summary>
	private static AnsiOutputOptions AnsiOptionsFor(ProtocolCapabilities capabilities, AnsiColorDepth depth,
		RenderPictureSource? pictures) =>
		new(depth, capabilities.Format == OutputFormat.Ansi ? capabilities.Features : TerminalFeatures.None)
		{
			Terminal = capabilities.Format == OutputFormat.Ansi ? capabilities.Terminal : null,
			Pictures = pictures,
			CellWidth = capabilities.CellWidth > 0 ? capabilities.CellWidth : 10,
			CellHeight = capabilities.CellHeight > 0 ? capabilities.CellHeight : 20
		};

	/// <summary>
	/// The registry for an ANSI client: shared across connections sent the same colour and features, and made
	/// for the one render when it draws pictures, since the picture source belongs to that render.
	/// </summary>
	private static MarkupRegistry AnsiWireFor(AnsiOutputOptions options) =>
		options.Pictures is not null
			? MarkupRegistry.Default.WithAnsiOutput(options)
			: AnsiWires.GetOrAdd((options.ColorDepth, options.Features),
				static key => MarkupRegistry.Default.WithAnsiOutput(new AnsiOutputOptions(key.Depth, key.Features)));

	private static readonly ConcurrentDictionary<(AnsiColorDepth Depth, TerminalFeatures Features), MarkupRegistry> AnsiWires = new();

	/// <summary>Each theme a player has set, as the layout theme it makes, or null for one that no longer reads.</summary>
	private static readonly ConcurrentDictionary<string, LayoutTheme?> ReaderThemes = new(StringComparer.Ordinal);

	/// <summary>The layout theme <paramref name="theme"/> makes, or null for none.</summary>
	private static LayoutTheme? ReaderTheme(string? theme)
	{
		if (string.IsNullOrWhiteSpace(theme)) return null;
		// A handful of themes are in use at once; past that, start again rather than grow without bound.
		if (ReaderThemes.Count > 256) ReaderThemes.Clear();
		return ReaderThemes.GetOrAdd(theme, static spec =>
		{
			// The engine sends only themes that read, but a theme is the player's own text: one that
			// fails here, however it fails, leaves their output in the game's theme rather than stopping it.
			try
			{
				return ThemePalette.TryParse(spec, out var palette, out _) ? palette!.ToLayoutTheme() : null;
			}
			catch (Exception)
			{
				return null;
			}
		});
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
				Utf8: capabilities.Utf8,
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
	/// The registry that renders Pueblo at <paramref name="depth"/>. Pueblo has its own links, so a link's
	/// colour part is written without OSC 8. Built on first use because <see cref="MarkupRegistry.Default"/> is installed at
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
