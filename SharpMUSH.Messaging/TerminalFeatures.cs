// NOTE: lives here beside TerminalCapabilityReader, for the same reason: the ConnectionServer and the
// engine both read it, and the ConnectionServer must not take a dependency on the full Library.

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// What a connection's terminal is sent beyond colour. The same flags as MarkupString's
/// <c>TerminalFeatures</c>, kept apart so this assembly need not reference the markup packages.
/// </summary>
[Flags]
public enum TerminalOutputFeatures
{
	None = 0,

	/// <summary>A URL link as an OSC 8 hyperlink.</summary>
	Hyperlinks = 1,

	/// <summary>A command link as an MSLP link, which the client sends back when clicked.</summary>
	CommandLinks = 2,

	/// <summary>Pictures through the Kitty graphics protocol.</summary>
	KittyGraphics = 4,

	/// <summary>Pictures through iTerm2's inline images.</summary>
	InlineImages = 8,

	/// <summary>Pictures as sixel graphics.</summary>
	Sixel = 16,

	/// <summary>Pictures as coloured half-block characters.</summary>
	BlockArt = 32,

	/// <summary>Every way of drawing a picture.</summary>
	Pictures = KittyGraphics | InlineImages | Sixel | BlockArt,
}

/// <summary>What a terminal answered when it was asked (<c>SOCKSET graphics=detect</c>).</summary>
/// <param name="KittyGraphics">Whether it answered the Kitty graphics query; null when that was not settled.</param>
/// <param name="Sixel">Whether its device attributes list sixel.</param>
/// <param name="Version">What it calls itself in reply to XTVERSION, or null.</param>
/// <param name="CellWidth">Its character cell's width in pixels, or zero when it did not say.</param>
/// <param name="CellHeight">Its character cell's height in pixels, or zero when it did not say.</param>
public sealed record TerminalProbeResult(bool? KittyGraphics, bool Sixel, string? Version, int CellWidth, int CellHeight);

/// <summary>The values <c>SOCKSET graphics</c> takes.</summary>
public static class TerminalGraphics
{
	public const string Kitty = "kitty";
	public const string Iterm2 = "iterm2";
	public const string Sixel = "sixel";
	public const string Blocks = "blocks";
	public const string Off = "off";

	/// <summary>The feature a pinned method turns on, or null for a value that is not one.</summary>
	public static TerminalOutputFeatures? FeatureOf(string? method) => method switch
	{
		Kitty => TerminalOutputFeatures.KittyGraphics,
		Iterm2 => TerminalOutputFeatures.InlineImages,
		Sixel => TerminalOutputFeatures.Sixel,
		Blocks => TerminalOutputFeatures.BlockArt,
		Off => TerminalOutputFeatures.None,
		_ => null
	};
}

/// <summary>
/// Works out what a connection's terminal is sent beyond colour: from the terminal types it reported,
/// from what it answered when asked, and from what the player pinned with <c>SOCKSET</c>.
/// <para>
/// Nothing here is sent on a guess. Each of these sequences is ignored by a terminal that does not know
/// it, but a MUD client that is not a terminal emulator may print it, so a feature is on only for a
/// terminal that names itself as one known to have it, one that answered the question, or a pin.
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

	/// <summary>The metadata key holding a <c>SOCKSET graphics</c> pin: one of <see cref="TerminalGraphics"/>.</summary>
	public const string GraphicsKey = "GRAPHICS";

	/// <summary>Metadata keys holding what the terminal answered when asked.</summary>
	public const string ProbeKittyKey = "TerminalKitty";
	public const string ProbeSixelKey = "TerminalSixel";
	public const string ProbeVersionKey = "TerminalVersion";
	public const string ProbeCellSizeKey = "TerminalCellSize";

	/// <summary>The MTTS capability name for bit 1024, as TelnetNegotiationCore expands it.</summary>
	private const string MttsMslp = "MSLP";

	/// <summary>Terminals that draw OSC 8 hyperlinks, by the names they report.</summary>
	private static readonly string[] HyperlinkTerminals =
		["xterm-kitty", "kitty", "xterm-ghostty", "ghostty", "wezterm", "foot", "alacritty", "contour", "mudlet"];

	/// <summary>Terminals that speak the Kitty graphics protocol, by name.</summary>
	private static readonly string[] KittyTerminals = ["xterm-kitty", "kitty", "xterm-ghostty", "ghostty"];

	/// <summary>Terminals that draw sixel, by name.</summary>
	private static readonly string[] SixelTerminals = ["foot", "mlterm", "contour"];

	/// <summary>
	/// What <paramref name="terminalTypes"/> and <paramref name="probe"/> say the terminal can do. A probe's
	/// answer about Kitty wins over the name: a terminal that called itself kitty and did not answer the
	/// question is reached through something that does not pass the protocol on, such as a multiplexer.
	/// Half-block art is never detected, since it is only text and only the player knows whether they want it.
	/// </summary>
	public static TerminalOutputFeatures Detect(IReadOnlyList<string> terminalTypes, TerminalProbeResult? probe)
	{
		ArgumentNullException.ThrowIfNull(terminalTypes);

		var features = TerminalOutputFeatures.None;
		if (terminalTypes.Any(type => Named(type, HyperlinkTerminals))) features |= TerminalOutputFeatures.Hyperlinks;
		if (terminalTypes.Any(type => string.Equals(type.Trim(), MttsMslp, StringComparison.OrdinalIgnoreCase)))
			features |= TerminalOutputFeatures.CommandLinks;

		var kitty = probe?.KittyGraphics ?? terminalTypes.Any(type => Named(type, KittyTerminals));
		if (kitty) features |= TerminalOutputFeatures.KittyGraphics;

		var version = probe?.Version ?? string.Empty;
		if (terminalTypes.Any(type => Named(type, ["wezterm"]))
			|| version.Contains("iTerm2", StringComparison.OrdinalIgnoreCase)
			|| version.Contains("WezTerm", StringComparison.OrdinalIgnoreCase))
			features |= TerminalOutputFeatures.InlineImages | TerminalOutputFeatures.Hyperlinks;

		if (probe?.Sixel == true || terminalTypes.Any(type => Named(type, SixelTerminals)))
			features |= TerminalOutputFeatures.Sixel;

		return features;
	}

	/// <summary>
	/// What the connection is sent: <paramref name="detected"/>, with each pin in place of what was detected.
	/// A screen reader is sent no feature it did not pin, and pictures not at all, since it reads the
	/// picture's description. Kitty pictures and half-block art are characters outside Latin-1, so output
	/// not written in UTF-8 (<paramref name="utf8"/>) carries neither, pinned or not.
	/// </summary>
	public static TerminalOutputFeatures Resolve(TerminalOutputFeatures detected, bool? hyperlinksPin,
		bool? commandLinksPin, string? graphicsPin, bool utf8, bool screenReader)
	{
		var features = screenReader ? TerminalOutputFeatures.None : detected;
		features = Pin(features, TerminalOutputFeatures.Hyperlinks, hyperlinksPin);
		features = Pin(features, TerminalOutputFeatures.CommandLinks, commandLinksPin);

		if (TerminalGraphics.FeatureOf(graphicsPin) is { } pinned)
			features = (features & ~TerminalOutputFeatures.Pictures) | pinned;

		if (screenReader) features &= ~TerminalOutputFeatures.Pictures;
		if (!utf8) features &= ~(TerminalOutputFeatures.KittyGraphics | TerminalOutputFeatures.BlockArt);

		return features;
	}

	/// <summary>
	/// How a connection with <paramref name="features"/> is sent a picture, by the name <c>SOCKSET graphics</c>
	/// takes: the method the renderer picks first (Kitty, then iTerm2, then sixel, then half-blocks), or
	/// <c>off</c> for text art.
	/// </summary>
	public static string GraphicsName(TerminalOutputFeatures features) =>
		features.HasFlag(TerminalOutputFeatures.KittyGraphics) ? TerminalGraphics.Kitty
		: features.HasFlag(TerminalOutputFeatures.InlineImages) ? TerminalGraphics.Iterm2
		: features.HasFlag(TerminalOutputFeatures.Sixel) ? TerminalGraphics.Sixel
		: features.HasFlag(TerminalOutputFeatures.BlockArt) ? TerminalGraphics.Blocks
		: TerminalGraphics.Off;

	/// <summary>What a connection is sent, read from its metadata the way the renderer reads its capabilities.</summary>
	public static TerminalOutputFeatures For(IReadOnlyDictionary<string, string> metadata)
	{
		ArgumentNullException.ThrowIfNull(metadata);

		var types = metadata.GetValueOrDefault(TerminalCapabilityReader.TerminalTypesKey, "")
			.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var terminal = TerminalCapabilityReader.Read(types);

		return Resolve(Detect(types, ProbeOf(metadata)), PinOf(metadata, HyperlinksKey), PinOf(metadata, CommandLinksKey),
			metadata.GetValueOrDefault(GraphicsKey), Utf8(metadata), terminal.ScreenReader);
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
		return new TerminalProbeResult(kitty is null ? null : kitty == "1", sixel == "1", version, width, height);
	}

	/// <summary>A yes/no pin stored under <paramref name="key"/>, or null for none.</summary>
	public static bool? PinOf(IReadOnlyDictionary<string, string> metadata, string key) =>
		metadata.GetValueOrDefault(key) switch
		{
			"1" => true,
			"0" => false,
			_ => null
		};

	/// <summary>
	/// Whether the connection's output is written in UTF-8: the charset it negotiated when one is recorded,
	/// and otherwise yes, which is what the socket server writes by default. Not the MTTS UTF-8 claim, which
	/// most terminals never make and which governs only whether box drawing is ASCII.
	/// </summary>
	private static bool Utf8(IReadOnlyDictionary<string, string> metadata) =>
		metadata.GetValueOrDefault("CHARSET") is not { Length: > 0 } charset
		|| charset.Replace("-", "").Equals("UTF8", StringComparison.OrdinalIgnoreCase);

	private static TerminalOutputFeatures Pin(TerminalOutputFeatures features, TerminalOutputFeatures feature, bool? pin) =>
		pin switch
		{
			true => features | feature,
			false => features & ~feature,
			null => features
		};

	/// <summary>Whether <paramref name="type"/> is one of <paramref name="names"/>, ignoring case.</summary>
	private static bool Named(string type, string[] names) =>
		names.Any(name => string.Equals(type.Trim(), name, StringComparison.OrdinalIgnoreCase));
}
