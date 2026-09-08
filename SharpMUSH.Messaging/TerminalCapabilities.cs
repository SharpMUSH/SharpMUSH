// NOTE: lives here rather than in SharpMUSH.Library, on the same grounds as ConnectionRetryPolicy —
// the ConnectionServer needs it to set ProtocolCapabilities and must not take a dependency on the
// full Library. The SharpMUSH.Library.* namespace is preserved so consumers are unchanged.

using System.Collections.Immutable;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// The colour depth a connection is rendered at — the values <c>SOCKSET colorstyle</c> accepts, and
/// the token <c>terminfo()</c> is documented to include for every connection.
/// </summary>
public static class ColorStyles
{
	public const string Plain = "plain";
	public const string Hilite = "hilite";
	public const string SixteenColor = "16color";
	public const string Xterm256 = "xterm256";

	/// <summary>
	/// 24-bit RGB. Not a PennMUSH colour style — PennMUSH predates clients that render
	/// <c>ESC[38;2;r;g;b</c> — but SharpMUSH's renderer emits those sequences for hex <c>ansi()</c>
	/// codes and for every syntax-highlighted help block, so there has to be a style that means "send
	/// them as they are" and one that means "map them down".
	/// </summary>
	public const string Truecolor = "truecolor";
}

/// <summary>
/// What a client's RFC 1091 terminal types say it can display.
/// </summary>
/// <param name="Ansi">The client renders SGR colour at all.</param>
/// <param name="Xterm256">The client renders 256-colour SGR sequences (<c>ESC[38;5;n</c>).</param>
/// <param name="Truecolor">The client renders 24-bit RGB SGR sequences (<c>ESC[38;2;r;g;b</c>).</param>
/// <param name="Utf8">The client claimed UTF-8.</param>
/// <param name="ScreenReader">
/// The client is a screen reader. Colour is meaningless to it, so it is rendered plain.
/// </param>
public readonly record struct TerminalCapabilities(
	bool Ansi,
	bool Xterm256,
	bool Truecolor,
	bool Utf8,
	bool ScreenReader)
{
	/// <summary>What is assumed of a client that reported no terminal types at all.</summary>
	public static TerminalCapabilities Unknown { get; } =
		new(Ansi: true, Xterm256: false, Truecolor: false, Utf8: false, ScreenReader: false);

	/// <summary>
	/// The colour style these capabilities imply, when the player has not pinned one with
	/// <c>SOCKSET colorstyle</c>. A screen reader gets plain text however much colour it claims;
	/// otherwise it is the deepest thing the client says it can render.
	/// </summary>
	public string ColorStyle => this switch
	{
		{ ScreenReader: true } => ColorStyles.Plain,
		{ Truecolor: true } => ColorStyles.Truecolor,
		{ Xterm256: true } => ColorStyles.Xterm256,
		{ Ansi: true } => ColorStyles.SixteenColor,
		_ => ColorStyles.Hilite
	};
}

/// <summary>
/// Reads a client's capabilities out of the terminal types it reported.
/// <para>
/// Two sources, because clients disagree about which they use. MTTS
/// (https://tintin.mudhalla.net/protocols/mtts/) sends a bitvector as a third terminal type, which
/// TelnetNegotiationCore has already expanded into names like <c>256 COLORS</c> by the time the
/// list reaches here. Everything else — the great majority of terminals — sends only a termcap
/// name, and <c>xterm-256color</c> is as much a capability claim as the bitvector is.
/// </para>
/// <para>
/// Claims are unioned across the whole list rather than taken from one entry: MTTS puts the client
/// name first and the bitvector third, and a client that sends both a termcap name and a bitvector
/// means both.
/// </para>
/// </summary>
public static class TerminalCapabilityReader
{
	/// <summary>
	/// MTTS capability names, as <c>MttsCapabilityNames.Expand</c> writes them. Matched
	/// case-insensitively and only as whole entries, so a terminal genuinely named "ANSI" — RFC 1091
	/// lets one exist — is read the same way, which is what it means anyway.
	/// </summary>
	private const string MttsAnsi = "ANSI";
	private const string MttsVt100 = "VT100";
	private const string MttsUtf8 = "UTF8";
	private const string Mtts256Colors = "256 COLORS";
	private const string MttsTruecolor = "TRUECOLOR";
	private const string MttsScreenReader = "SCREEN_READER";

	/// <summary>
	/// RFC 1091's own word for a terminal that will not name itself, which
	/// <c>TerminalTypeProtocol.UnknownTerminalType</c> answers with. Spelled out rather than
	/// referenced so this assembly need not take the telnet library as a dependency.
	/// </summary>
	private const string UnknownTerminalType = "UNKNOWN";

	/// <summary>
	/// The connection metadata key holding the reported terminal types, tab-separated. Tabs rather
	/// than spaces because MTTS capability names contain spaces ("256 COLORS"), so a space-separated
	/// list could not be split back apart.
	/// </summary>
	public const string TerminalTypesKey = "TerminalTypes";

	/// <summary>The connection metadata key holding an explicit <c>SOCKSET colorstyle</c> pin.</summary>
	public const string ColorStyleKey = "COLORSTYLE";

	/// <summary>
	/// What a connection's recorded terminal types claim. A connection that reported none — never
	/// asked, or refused — is <see cref="TerminalCapabilities.Unknown"/>.
	/// </summary>
	public static TerminalCapabilities Read(IReadOnlyDictionary<string, string> metadata)
	{
		ArgumentNullException.ThrowIfNull(metadata);

		return Read(metadata.GetValueOrDefault(TerminalTypesKey, "")
			.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
	}

	/// <summary>
	/// The colour style a connection is rendered at: the style <c>SOCKSET colorstyle</c> pinned, or —
	/// when nothing is pinned — the one the client's own terminal types imply. <c>terminfo()</c>
	/// reports this, and <c>SOCKSET</c> shows the unpinned case as "auto (&lt;style&gt;)".
	/// </summary>
	public static string ColorStyleFor(IReadOnlyDictionary<string, string> metadata)
	{
		ArgumentNullException.ThrowIfNull(metadata);

		var pinned = metadata.GetValueOrDefault(ColorStyleKey, "");
		return string.IsNullOrEmpty(pinned) ? Read(metadata).ColorStyle : pinned;
	}

	/// <summary>
	/// Reads what <paramref name="terminalTypes"/> claims. An empty list is
	/// <see cref="TerminalCapabilities.Unknown"/> — the client was never asked, or refused to say.
	/// </summary>
	public static TerminalCapabilities Read(IReadOnlyList<string> terminalTypes)
	{
		ArgumentNullException.ThrowIfNull(terminalTypes);

		if (terminalTypes.Count == 0)
		{
			return TerminalCapabilities.Unknown;
		}

		var entries = terminalTypes
			.Where(type => !string.IsNullOrWhiteSpace(type))
			.Select(type => type.Trim())
			.ToImmutableArray();

		if (entries.IsEmpty)
		{
			return TerminalCapabilities.Unknown;
		}

		var screenReader = Has(entries, MttsScreenReader);

		// MTTS keeps these two bits apart, and so does this: a client that renders 256 palette entries
		// is not thereby able to render 24-bit RGB, and sending it ESC[38;2;r;g;b produces garbage
		// rather than an approximation. Truecolour implies 256 — every terminal that took the trouble
		// to implement direct colour already had the palette — but never the reverse.
		var truecolor = Has(entries, MttsTruecolor) || entries.Any(MentionsDirectColor);

		var xterm256 = truecolor
									 || Has(entries, Mtts256Colors)
									 || entries.Any(MentionsDeepColor);

		// A client that claims 256 colours claims colour; so does any termcap name that is not one of
		// the handful that mean "no colour". Being wrong in this direction costs a client that ignores
		// SGR a few stray escapes, while being wrong the other way silently strips every colour a
		// perfectly capable terminal asked for.
		var ansi = xterm256
							 || Has(entries, MttsAnsi)
							 || Has(entries, MttsVt100)
							 || entries.Any(MentionsColor);

		var utf8 = Has(entries, MttsUtf8);

		return new TerminalCapabilities(ansi, xterm256, truecolor, utf8, screenReader);
	}

	private static bool Has(ImmutableArray<string> entries, string name) =>
		entries.Any(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Termcap names claiming 24-bit RGB. <c>-direct</c> is terminfo's own suffix for a direct-colour
	/// entry (<c>xterm-direct</c>, <c>vte-direct</c>); the others are how clients spell it in a name
	/// they made up.
	/// </summary>
	private static bool MentionsDirectColor(string entry) =>
		entry.Contains("truecolor", StringComparison.OrdinalIgnoreCase)
		|| entry.Contains("24bit", StringComparison.OrdinalIgnoreCase)
		|| entry.Contains("direct", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Termcap names claiming more than 16 colours: <c>xterm-256color</c>, <c>screen-256color</c>,
	/// <c>tmux-256color</c>.
	/// </summary>
	private static bool MentionsDeepColor(string entry) =>
		entry.Contains("256", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Termcap names claiming colour at all, which is every name except the handful that mean the
	/// opposite: <c>-m</c> and <c>-mono</c> are the conventional suffixes for a monochrome variant,
	/// "dumb" is the terminfo name for a terminal with no capabilities, and RFC 1091's "UNKNOWN" is a
	/// client declining to say.
	/// <para>
	/// Deliberately a blocklist and not a whitelist of known-good names. A whitelist reads every MUD
	/// client that reports its own name — MUDLET, MUSHCLIENT, POTATO, ATLANTIS, none of which contain
	/// "xterm" or "color" — as monochrome, which is the expensive direction to be wrong in: it
	/// silently strips every colour a perfectly capable terminal asked for, while being wrong the
	/// other way costs a client that ignores SGR a few stray escapes.
	/// </para>
	/// </summary>
	private static bool MentionsColor(string entry) =>
		!entry.Contains("mono", StringComparison.OrdinalIgnoreCase)
		&& !entry.EndsWith("-m", StringComparison.OrdinalIgnoreCase)
		&& !entry.Equals("dumb", StringComparison.OrdinalIgnoreCase)
		&& !entry.Equals(UnknownTerminalType, StringComparison.OrdinalIgnoreCase);
}
