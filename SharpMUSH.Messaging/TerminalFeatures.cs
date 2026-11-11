// NOTE: lives here beside TerminalCapabilityReader, for the same reason: the ConnectionServer and the
// engine both read it, and the ConnectionServer must not take a dependency on the full Library.

using MarkupString.Ansi;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Utilities;

/// <summary>What a terminal answered when it was asked (<c>SOCKSET graphics=detect</c>).</summary>
public sealed record TerminalProbeResult
{
	/// <summary>Whether the terminal answered the Kitty query; null when unsettled.</summary>
	public bool? KittyGraphics { get; init; }

	/// <summary>Whether its device attributes list sixel; null when no answer arrived.</summary>
	[JsonPropertyName("Sixel")]
	public bool? SixelReply { get; init; }

	/// <summary>What the terminal calls itself in reply to XTVERSION, or null.</summary>
	public string? Version { get; init; }

	/// <summary>The character cell's width in pixels, or zero when unknown.</summary>
	public int CellWidth { get; init; }

	/// <summary>The character cell's height in pixels, or zero when unknown.</summary>
	public int CellHeight { get; init; }

	/// <summary>Creates a probe that distinguishes unknown from unsupported sixel.</summary>
	[JsonConstructor]
	public TerminalProbeResult(bool? kittyGraphics, bool? sixelReply, string? version, int cellWidth, int cellHeight)
	{
		KittyGraphics = kittyGraphics;
		SixelReply = sixelReply;
		Version = version;
		CellWidth = cellWidth;
		CellHeight = cellHeight;
	}

	/// <summary>The constructor published with plugin contract 3.0.0.</summary>
	public TerminalProbeResult(bool? KittyGraphics, bool Sixel, string? Version, int CellWidth, int CellHeight)
		: this(KittyGraphics, (bool?)Sixel, Version, CellWidth, CellHeight)
	{
	}

	/// <summary>
	/// The Boolean view published with plugin contract 3.0.0. Unknown replies read as false;
	/// use <see cref="SixelReply"/> to distinguish unknown from an explicit negative reply.
	/// </summary>
	[JsonIgnore]
	public bool Sixel
	{
		get => SixelReply == true;
		init => SixelReply = value;
	}

	/// <summary>The deconstruction published with plugin contract 3.0.0.</summary>
	public void Deconstruct(out bool? kittyGraphics, out bool sixel, out string? version,
		out int cellWidth, out int cellHeight)
	{
		kittyGraphics = KittyGraphics;
		sixel = Sixel;
		version = Version;
		cellWidth = CellWidth;
		cellHeight = CellHeight;
	}
}

/// <summary>
/// What the player set with <c>SOCKSET</c> about their terminal. Links are worked out from the terminal unless
/// pinned; pictures and moving pictures are sent only once the player turns them on.
/// </summary>
/// <param name="Hyperlinks">A <c>SOCKSET hyperlinks</c> pin, or null for auto.</param>
/// <param name="CommandLinks">A <c>SOCKSET commandlinks</c> pin, or null for auto.</param>
/// <param name="Graphics">A <c>SOCKSET graphics</c> setting (<see cref="TerminalGraphics"/>), or null for off.</param>
/// <param name="Animation">Whether <c>SOCKSET animation</c> is on.</param>
/// <param name="Terminal">The terminal the player named with <c>SOCKSET terminal</c> (a <see cref="TerminalProfile.Id"/>), or null.</param>
/// <param name="ScreenReader">
/// A <c>SCREENREADER</c> (or <c>SOCKSET screenreader</c>) pin, or null for whatever the client said through MTTS.
/// </param>
/// <param name="Charset">The character set the player named with <c>SOCKSET charset</c> (one of <see cref="TerminalCharsets"/>), or null for auto.</param>
/// <param name="StripAccents">Whether <c>SOCKSET stripaccents</c> is on, which sends ASCII whatever the client says.</param>
/// <param name="AltText">
/// A <c>SOCKSET alttext</c> pin: on sends a picture the terminal does not draw as its description, off as its address.
/// Null for a screen reader's choice: descriptions for one, addresses for everyone else.
/// </param>
public sealed record TerminalPins(
	bool? Hyperlinks = null,
	bool? CommandLinks = null,
	string? Graphics = null,
	bool Animation = false,
	string? Terminal = null,
	bool? ScreenReader = null,
	string? Charset = null,
	bool StripAccents = false,
	bool? AltText = null)
{
	/// <summary>Nothing set: links worked out from the terminal, no pictures.</summary>
	public static TerminalPins None { get; } = new();

	/// <summary>The pins stored in a connection's <paramref name="metadata"/>.</summary>
	public static TerminalPins Of(IReadOnlyDictionary<string, string> metadata)
	{
		ArgumentNullException.ThrowIfNull(metadata);
		return new TerminalPins(
			TerminalFeatureReader.PinOf(metadata, TerminalFeatureReader.HyperlinksKey),
			TerminalFeatureReader.PinOf(metadata, TerminalFeatureReader.CommandLinksKey),
			metadata.GetValueOrDefault(TerminalFeatureReader.GraphicsKey),
			TerminalFeatureReader.PinOf(metadata, TerminalFeatureReader.AnimationKey) == true,
			metadata.GetValueOrDefault(TerminalFeatureReader.TerminalKey),
			TerminalFeatureReader.PinOf(metadata, TerminalCapabilityReader.ScreenReaderKey),
			TerminalCharsets.Parse(metadata.GetValueOrDefault(TerminalFeatureReader.CharsetKey)),
			TerminalFeatureReader.PinOf(metadata, TerminalFeatureReader.StripAccentsKey) == true,
			TerminalFeatureReader.PinOf(metadata, TerminalFeatureReader.AltTextKey));
	}
}

/// <summary>
/// The character sets output is written in. A client without UTF-8 is sent each character it cannot show as
/// the nearest one it can (<see cref="MarkupString.AsciiFold"/>), so a middle dot reaches it as <c>*</c>
/// rather than as <c>?</c> or as two bytes it shows as two wrong characters.
/// </summary>
public static class TerminalCharsets
{
	public const string Utf8 = "utf-8";
	public const string Latin1 = "latin-1";
	public const string Ascii = "ascii";

	/// <summary>The character set <paramref name="name"/> names, however it is spelled, or null for one that is not one of these.</summary>
	public static string? Parse(string? name) => name?.Trim().ToLowerInvariant().Replace("_", "-") switch
	{
		"utf-8" or "utf8" => Utf8,
		"latin-1" or "latin1" or "iso-8859-1" or "iso8859-1" or "l1" => Latin1,
		"ascii" or "us-ascii" or "us" => Ascii,
		_ => null
	};

	/// <summary>
	/// The character set a connection is written in: ASCII under <c>SOCKSET stripaccents</c>; otherwise the one
	/// <c>SOCKSET charset</c> names; otherwise the one telnet CHARSET negotiation settled on; otherwise UTF-8 for a
	/// client that claims it, ASCII for one that does not.
	/// </summary>
	/// <param name="pins">What the player set.</param>
	/// <param name="negotiated">What CHARSET negotiation settled on, or null when it never did.</param>
	/// <param name="claimsUtf8">
	/// Whether the client claims UTF-8 (MTTS) or is a terminal known to show it.
	/// </param>
	public static string Resolve(TerminalPins? pins, string? negotiated, bool claimsUtf8) =>
		pins is { StripAccents: true } ? Ascii
		: pins?.Charset is { } pinned ? pinned
		: Parse(negotiated) is { } settled ? settled
		: claimsUtf8 ? Utf8
		: Ascii;
}

/// <summary>The values <c>SOCKSET graphics</c> takes.</summary>
public static class TerminalGraphics
{
	/// <summary>Every way the terminal can draw a picture; the renderer picks the best.</summary>
	public const string Auto = "auto";
	public const string Kitty = "kitty";
	public const string Iterm2 = "iterm2";
	public const string Sixel = "sixel";
	public const string Blocks = "blocks";
	public const string Off = "off";

	/// <summary>The feature a method turns on, or null for a value that is not one.</summary>
	public static TerminalFeatures? FeatureOf(string? method) => method switch
	{
		Kitty => TerminalFeatures.KittyGraphics,
		Iterm2 => TerminalFeatures.InlineImages,
		Sixel => TerminalFeatures.Sixel,
		Blocks => TerminalFeatures.BlockArt,
		Off => TerminalFeatures.None,
		_ => null
	};
}

/// <summary>
/// Works out what a connection's terminal is sent beyond colour: which terminal it is (from what it reported,
/// what it answered when asked, or what the player named), what that terminal can do, and what the player
/// turned on with <c>SOCKSET</c>.
/// <para>
/// Links are sent to a terminal known to draw them. Pictures and moving pictures are sent only when the player
/// asks for them: a terminal is often reached through something that changes what it shows (a multiplexer, an
/// ssh hop, a setting left off), and only the player sees the result.
/// </para>
/// <para>
/// Like <see cref="TerminalCapabilityReader.ResolveColorStyle"/>, the engine and the renderer both call
/// <see cref="Resolve"/>, so what <c>terminfo()</c> reports and what goes on the wire cannot drift.
/// </para>
/// </summary>
public static class TerminalFeatureReader
{
	/// <summary>The metadata key holding a <c>SOCKSET hyperlinks</c> pin: <c>1</c> or <c>0</c>.</summary>
	public const string HyperlinksKey = "HYPERLINKS";

	/// <summary>The metadata key holding a <c>SOCKSET commandlinks</c> pin: <c>1</c> or <c>0</c>.</summary>
	public const string CommandLinksKey = "COMMANDLINKS";

	/// <summary>The metadata key holding a <c>SOCKSET graphics</c> setting: one of <see cref="TerminalGraphics"/>.</summary>
	public const string GraphicsKey = "GRAPHICS";

	/// <summary>The metadata key holding <c>SOCKSET animation</c>: <c>1</c>, or absent for off.</summary>
	public const string AnimationKey = "ANIMATION";

	/// <summary>The metadata key holding <c>SOCKSET terminal</c>: a <see cref="TerminalProfile.Id"/>.</summary>
	public const string TerminalKey = "TERMINAL";

	/// <summary>The metadata key holding <c>SOCKSET charset</c>: one of <see cref="TerminalCharsets"/>, or absent for auto.</summary>
	public const string CharsetKey = "CHARSET";

	/// <summary>The metadata key holding PennMUSH's <c>SOCKSET stripaccents</c>: <c>1</c> or <c>0</c>.</summary>
	public const string StripAccentsKey = "STRIPACCENTS";

	/// <summary>The metadata key holding a <c>SOCKSET alttext</c> pin: <c>1</c> or <c>0</c>.</summary>
	public const string AltTextKey = "ALTTEXT";

	/// <summary>Metadata keys holding what the terminal answered when asked.</summary>
	public const string ProbeKittyKey = "TerminalKitty";
	public const string ProbeSixelKey = "TerminalSixel";
	public const string ProbeVersionKey = "TerminalVersion";
	public const string ProbeCellSizeKey = "TerminalCellSize";

	/// <summary>The MTTS capability name for bit 1024, as TelnetNegotiationCore expands it.</summary>
	private const string MttsMslp = "MSLP";

	/// <summary>Clients that are not terminals but draw OSC 8 hyperlinks, by the names they report.</summary>
	private static readonly string[] HyperlinkClients = ["mudlet"];

	private const TerminalFeatures Links = TerminalFeatures.Hyperlinks | TerminalFeatures.CommandLinks;

	/// <summary>
	/// The terminal: the one the player named in <paramref name="terminalPin"/>, else the one that answered
	/// XTVERSION in <paramref name="probe"/>, else the first of <paramref name="terminalTypes"/>, which is the
	/// client's own name. The later entries are what it claims to support (MTTS's <c>XTERM</c>, <c>ANSI</c> or
	/// <c>VT100</c>, or the terminal a MUD client runs in) and never identify it: a MUD client that sends
	/// <c>XTERM</c> second is not an xterm and would print a picture sent to one. A bare <c>xterm</c> names no
	/// terminal either: PuTTY, plain telnet and MUD clients report it, and xterm itself draws Sixel only when built
	/// to emulate a VT340. A player names xterm with a pin, or it answers XTVERSION.
	/// </summary>
	public static TerminalProfile? Identify(IReadOnlyList<string> terminalTypes, TerminalProbeResult? probe, string? terminalPin)
	{
		ArgumentNullException.ThrowIfNull(terminalTypes);
		return TerminalProfile.Find(terminalPin)
			?? TerminalProfile.Identify(probe?.Version)
			?? (TerminalProfile.Identify(terminalTypes.FirstOrDefault()) is { } named && named != TerminalProfile.XTerm
				? named
				: null);
	}

	/// <summary>
	/// What the terminal can do: what <paramref name="terminal"/> is known for, corrected by what it answered in
	/// <paramref name="probe"/>. A Kitty or sixel answer wins over the name, since a terminal that has the
	/// protocol may explicitly report it disabled. An unanswered query preserves known capabilities.
	/// Half-block art is any UTF-8 terminal's.
	/// </summary>
	public static TerminalFeatures Detect(TerminalProfile? terminal, IReadOnlyList<string> terminalTypes, TerminalProbeResult? probe)
	{
		ArgumentNullException.ThrowIfNull(terminalTypes);

		var features = (terminal?.Features ?? TerminalFeatures.None) | TerminalFeatures.BlockArt;
		if (terminalTypes.Any(type => Named(type, HyperlinkClients))) features |= TerminalFeatures.Hyperlinks;
		if (terminalTypes.Any(type => Named(type, [MttsMslp]))) features |= TerminalFeatures.CommandLinks;

		if (probe is not null)
		{
			if (probe.SixelReply is { } sixel) features = Set(features, TerminalFeatures.Sixel, sixel);
			if (probe.KittyGraphics is { } kitty) features = Set(features, TerminalFeatures.KittyGraphics, kitty);
		}

		return features;
	}

	/// <summary>
	/// What the connection is sent. Links are <paramref name="detected"/>'s, with each pin in place of what was
	/// detected. Pictures are none until the player sets <c>graphics</c>: <c>auto</c> sends every way the terminal
	/// draws them, a method sends that one. Moving pictures need <c>animation</c> on as well, and a terminal not
	/// known to lack them. A screen reader is sent no link it did not pin and no picture, since it reads the
	/// picture's description. Kitty pictures and half-block art are characters outside Latin-1, so output not
	/// written in UTF-8 (<paramref name="utf8"/>) carries neither.
	/// </summary>
	public static TerminalFeatures Resolve(TerminalFeatures detected, TerminalProfile? terminal, TerminalPins pins,
		bool utf8, bool screenReader)
	{
		ArgumentNullException.ThrowIfNull(pins);

		var features = screenReader ? TerminalFeatures.None : detected & Links;
		features = Pin(features, TerminalFeatures.Hyperlinks, pins.Hyperlinks);
		features = Pin(features, TerminalFeatures.CommandLinks, pins.CommandLinks);

		var pictures = pins.Graphics == TerminalGraphics.Auto
			? detected & TerminalFeatures.Pictures
			: TerminalGraphics.FeatureOf(pins.Graphics) ?? TerminalFeatures.None;
		if (screenReader) pictures = TerminalFeatures.None;
		if (!utf8) pictures &= ~(TerminalFeatures.KittyGraphics | TerminalFeatures.BlockArt);

		if (pins.Animation && (pictures & (TerminalFeatures.KittyGraphics | TerminalFeatures.InlineImages)) != 0
			&& (terminal is null || terminal.Features.HasFlag(TerminalFeatures.MovingPictures)))
			pictures |= TerminalFeatures.MovingPictures;

		return features | pictures;
	}

	/// <summary>
	/// How a connection with <paramref name="features"/> is sent a picture, by the name <c>SOCKSET graphics</c>
	/// takes: the method the renderer picks first (Kitty, then iTerm2, then sixel, then half-blocks), or
	/// <c>off</c> for text art.
	/// </summary>
	public static string GraphicsName(TerminalFeatures features) =>
		features.HasFlag(TerminalFeatures.KittyGraphics) ? TerminalGraphics.Kitty
		: features.HasFlag(TerminalFeatures.InlineImages) ? TerminalGraphics.Iterm2
		: features.HasFlag(TerminalFeatures.Sixel) ? TerminalGraphics.Sixel
		: features.HasFlag(TerminalFeatures.BlockArt) ? TerminalGraphics.Blocks
		: TerminalGraphics.Off;

	/// <summary>The terminal a connection is, read from its metadata the way the renderer reads its capabilities.</summary>
	public static TerminalProfile? TerminalOf(IReadOnlyDictionary<string, string> metadata)
	{
		ArgumentNullException.ThrowIfNull(metadata);
		return Identify(TypesOf(metadata), ProbeOf(metadata), metadata.GetValueOrDefault(TerminalKey));
	}

	/// <summary>What a connection is sent, read from its metadata the way the renderer reads its capabilities.</summary>
	public static TerminalFeatures For(IReadOnlyDictionary<string, string> metadata)
	{
		ArgumentNullException.ThrowIfNull(metadata);

		var types = TypesOf(metadata);
		var probe = ProbeOf(metadata);
		var pins = TerminalPins.Of(metadata);
		var terminal = Identify(types, probe, pins.Terminal);

		return Resolve(Detect(terminal, types, probe), terminal, pins, Utf8(metadata),
			TerminalCapabilityReader.ReadAsSent(metadata).ScreenReader);
	}

	/// <summary>The terminal's answers recorded in <paramref name="metadata"/>, or null when it was never asked.</summary>
	public static TerminalProbeResult? ProbeOf(IReadOnlyDictionary<string, string> metadata)
	{
		// A question the terminal left unanswered is stored empty.
		var kitty = metadata.GetValueOrDefault(ProbeKittyKey) is { Length: > 0 } k ? k : null;
		var sixel = metadata.GetValueOrDefault(ProbeSixelKey);
		var version = metadata.GetValueOrDefault(ProbeVersionKey) is { Length: > 0 } v ? v : null;
		if (kitty is null && sixel is null && version is null) return null;

		var size = metadata.GetValueOrDefault(ProbeCellSizeKey, "").Split('x');
		var width = size.Length == 2 && int.TryParse(size[0], out var w) ? w : 0;
		var height = size.Length == 2 && int.TryParse(size[1], out var h) ? h : 0;
		return new TerminalProbeResult(kitty is null ? null : kitty == "1", sixel is null or "" ? null : sixel == "1", version, width, height);
	}

	/// <summary>A yes/no pin stored under <paramref name="key"/>, or null for none.</summary>
	public static bool? PinOf(IReadOnlyDictionary<string, string> metadata, string key) =>
		metadata.GetValueOrDefault(key) switch
		{
			"1" => true,
			"0" => false,
			_ => null
		};

	// Empty entries stay, so the first entry here is the first the client sent, as on the connection itself.
	private static string[] TypesOf(IReadOnlyDictionary<string, string> metadata) =>
		metadata.GetValueOrDefault(TerminalCapabilityReader.TerminalTypesKey, "") is { Length: > 0 } types
			? types.Split('\t', StringSplitOptions.TrimEntries)
			: [];

	/// <summary>
	/// Whether the connection is written in UTF-8 as far as the engine knows: what the player set, and otherwise
	/// yes. The socket server also knows what the client negotiated and claimed (<see cref="TerminalCharsets.Resolve"/>).
	/// </summary>
	private static bool Utf8(IReadOnlyDictionary<string, string> metadata) =>
		TerminalCharsets.Resolve(TerminalPins.Of(metadata), null, claimsUtf8: true) == TerminalCharsets.Utf8;

	private static TerminalFeatures Pin(TerminalFeatures features, TerminalFeatures feature, bool? pin) =>
		pin is { } on ? Set(features, feature, on) : features;

	private static TerminalFeatures Set(TerminalFeatures features, TerminalFeatures feature, bool on) =>
		on ? features | feature : features & ~feature;

	/// <summary>Whether <paramref name="type"/> is one of <paramref name="names"/>, ignoring case.</summary>
	private static bool Named(string type, string[] names) =>
		names.Any(name => string.Equals(type.Trim(), name, StringComparison.OrdinalIgnoreCase));
}
