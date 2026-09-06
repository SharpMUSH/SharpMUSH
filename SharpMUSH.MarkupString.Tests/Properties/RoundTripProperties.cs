using System.Globalization;
using System.Text;
using CsCheck;
using MarkupString.Ansi;
using MarkupString.Html;
namespace SharpMUSH.MarkupString.Tests.Properties;

/// <summary>
/// Properties that have to hold for every <see cref="MarkupText"/>, checked over generated texts
/// whose alphabet mixes ASCII, CJK, combining marks and surrogate-pair emoji, and whose runs carry
/// random ANSI and HTML layers.
/// </summary>
public class RoundTripProperties
{
	/// <summary>
	/// The fixed starting seed every sample runs from, so a failure here reproduces exactly. CsCheck
	/// seeds are twelve characters; it prints the shrunk counterexample's own seed in the failure
	/// message, and dropping that in here replays just that case.
	/// </summary>
	private const string Seed = "000000000000";

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static readonly MarkupFormat[] Formats =
	[
		MarkupFormat.Plain, MarkupFormat.Ansi, MarkupFormat.Html,
		MarkupFormat.Pueblo, MarkupFormat.Mxp, MarkupFormat.BBCode,
	];

	// ── Generators ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Text is drawn from these pieces rather than from random code units: a random UTF-16 string
	/// would be mostly lone surrogates and would say nothing about cluster handling. Here every
	/// piece is well formed on its own, and the interesting cases — a combining mark landing on
	/// whatever precedes it, a wide character, an astral pair, a joiner — arise from how they land
	/// next to each other.
	/// </summary>
	private static readonly string[] Alphabet =
	[
		"a", "Z", "q", " ", "7", "<", "&", "\"", "'", "-",
		"日", "本", "語",
		"\u0301", "\u0308",
		"é", "ñ",
		"\U0001F600", "\U0001F1EF\U0001F1F5",
		"\u200d",
	];

	private static readonly Gen<string> GenPiece =
		Gen.Int[0, Alphabet.Length - 1].Array[0, 8].Select(ix => string.Concat(ix.Select(i => Alphabet[i])));

	/// <remarks>
	/// Xterm indices start at 16 on purpose. The wire format writes a palette index as one integer
	/// for both <see cref="AnsiColor.Xterm"/> and <see cref="AnsiColor.Standard"/>, and the reader
	/// resolves 0-15 to the standard colour, so <c>Xterm(13)</c> comes back as
	/// <c>Standard(5, bright)</c> — the same colour, a different SGR spelling. That normalisation is
	/// deliberate and pinned by
	/// <c>AnsiMarkupCodecTests.RoundTrip_XtermIndexUnderSixteen_ComesBackAsTheStandardColour</c>;
	/// including those indices here would only re-fail the same known case.
	/// </remarks>
	private static readonly Gen<AnsiColor> GenColour = Gen.OneOf(
		Gen.Const<AnsiColor>(AnsiColor.Default.Instance),
		Gen.Select(Gen.Int[0, 7], Gen.Bool, (i, bright) => (AnsiColor)new AnsiColor.Standard((byte)i, bright)),
		Gen.Int[16, 255].Select(n => (AnsiColor)new AnsiColor.Xterm((byte)n)),
		Gen.Select(Gen.Byte, Gen.Byte, Gen.Byte, (r, g, b) => (AnsiColor)new AnsiColor.Rgb(r, g, b)));

	/// <summary>Two thirds of spans set no colour, so unstyled and half-styled runs stay common.</summary>
	private static readonly Gen<AnsiColor?> GenOptionalColour = Gen.OneOf(
		Gen.Const(default(AnsiColor?)),
		Gen.Const(default(AnsiColor?)),
		GenColour.Select(c => (AnsiColor?)c));

	/// <summary>
	/// Links are rare (the no-link case is weighted five to one) and cover both kinds, an
	/// unsafe scheme, a hint, and a target that needs encoding.
	/// </summary>
	private static readonly Gen<(string? Url, string? Text, LinkKind Kind)> GenLink = Gen.OneOfConst(
		((string?)null, (string?)null, LinkKind.Url),
		(null, null, LinkKind.Url),
		(null, null, LinkKind.Url),
		(null, null, LinkKind.Url),
		(null, null, LinkKind.Url),
		("https://example.com/x?a=1&b=2", null, LinkKind.Url),
		("http://example.org/y", "Hint <&>", LinkKind.Url),
		("javascript:alert(1)", null, LinkKind.Url),
		("+who", "Who?", LinkKind.Command),
		("look \"north\"", null, LinkKind.Command));

	private static readonly Gen<IMarkup> GenAnsiMarkup = Gen.Select(
		GenOptionalColour, GenOptionalColour, Gen.Bool.Array[9], GenLink,
		(fg, bg, flag, link) => (IMarkup)AnsiMarkup.Create(
			foreground: fg,
			background: bg,
			linkText: link.Text,
			linkUrl: link.Url,
			linkKind: link.Kind,
			bold: flag[0],
			faint: flag[1],
			italic: flag[2],
			underlined: flag[3],
			overlined: flag[4],
			blink: flag[5],
			inverted: flag[6],
			strikeThrough: flag[7],
			clear: flag[8]));

	private static readonly Gen<IMarkup> GenHtmlMarkup = Gen.Select(
		Gen.OneOfConst("b", "i", "u", "s", "send", "div"),
		Gen.OneOfConst<string?>(null, null, "class=\"x\"", "href=\"north\" hint=\"Go north\""),
		(tag, attributes) => (IMarkup)HtmlMarkup.Create(tag, attributes));

	private static readonly Gen<MarkupSet> GenSet =
		Gen.OneOf(GenAnsiMarkup, GenAnsiMarkup, GenHtmlMarkup).Array[1, 3]
			.Select(layers => MarkupSet.Of(layers.AsSpan()));

	private static readonly Gen<MarkupText> GenSegment = Gen.Select(
		GenPiece,
		Gen.OneOf(Gen.Const(default(MarkupSet?)), GenSet.Select(s => (MarkupSet?)s)),
		(text, set) => set is null ? MarkupText.Plain(text) : MarkupText.Wrap(set, text));

	/// <summary>
	/// A text of up to six segments. Building it by concatenation is what makes the runs
	/// non-overlapping and gap-covered without the test reaching for the internal constructor.
	/// </summary>
	private static readonly Gen<MarkupText> GenMarkupText =
		GenSegment.Array[0, 6].Select(parts => MarkupText.Concat(parts.AsSpan()));

	// ── Properties ───────────────────────────────────────────────────────────────

	// Each property is an Action<T> that throws on a violation: that is the CsCheck overload that
	// shrinks, so a failure arrives as the smallest input that still breaks the property, with the
	// message below attached. (The Func<T, string> overload is `classify`, not an assertion.)

	[Test]
	public void Serialization_RoundTrips_IdenticallyInEveryFormat()
	{
		Action<MarkupText> property = text =>
		{
			var json = MarkupTextSerializer.Serialize(text, Registry);
			var round = MarkupTextSerializer.Deserialize(json, Registry);

			foreach (var format in Formats)
			{
				var before = text.Render(format, Registry);
				var after = round.Render(format, Registry);
				if (!string.Equals(before, after, StringComparison.Ordinal))
				{
					throw new MarkupPropertyException($"{format.Name}: {Show(before)} != {Show(after)} (json {json})");
				}
			}
		};

		Check.Sample(GenMarkupText, property, seed: Seed, iter: 5000);
	}

	[Test]
	public void Substring_NeverYieldsALoneSurrogate()
	{
		Action<(MarkupText Text, int Start, int Length)> property = t =>
		{
			var cut = t.Text.Substring(t.Start, t.Length).Text;
			if (HasLoneSurrogate(cut))
			{
				throw new MarkupPropertyException(
					$"substring({t.Start}, {t.Length}) of {Show(t.Text.Text)} gave {Show(cut)}");
			}
		};

		Check.Sample(Gen.Select(GenMarkupText, Gen.Int[-4, 200], Gen.Int[-4, 200]), property, seed: Seed, iter: 5000);
	}

	[Test]
	public void SplitAtAGraphemeBoundary_ConcatenatesBackToTheOriginal()
	{
		Action<(MarkupText Text, int At)> property = t =>
		{
			var at = Graphemes.SnapStart(t.Text.Text, Math.Clamp(t.At, 0, t.Text.Length));
			var rejoined = MarkupText.Concat(t.Text.Substring(0, at), t.Text.Substring(at)).Text;
			if (!string.Equals(rejoined, t.Text.Text, StringComparison.Ordinal))
			{
				throw new MarkupPropertyException(
					$"split at {at} of {Show(t.Text.Text)} gave {Show(rejoined)}");
			}
		};

		Check.Sample(Gen.Select(GenMarkupText, Gen.Int[0, 200]), property, seed: Seed, iter: 5000);
	}

	[Test]
	public void PadWithTruncation_NeverExceedsTheRequestedWidth()
	{
		Action<(MarkupText Text, int Width, char Fill)> property = t =>
		{
			var fill = MarkupText.Plain(t.Fill.ToString());
			var padded = t.Text.Pad(fill, t.Width, PadType.Right, TruncationType.Truncate);
			var cells = DisplayWidth.Of(padded.Text);
			if (cells > t.Width)
			{
				throw new MarkupPropertyException(
					$"pad('{t.Fill}', {t.Width}) of {Show(t.Text.Text)} gave {cells} cells: {Show(padded.Text)}");
			}
		};

		Check.Sample(
			Gen.Select(GenMarkupText, Gen.Int[0, 60], Gen.OneOfConst('.', '-', '*', ' ')),
			property, seed: Seed, iter: 5000);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	/// <summary>True when any surrogate in the string is missing its partner.</summary>
	private static bool HasLoneSurrogate(string text)
	{
		for (var i = 0; i < text.Length; i++)
		{
			if (char.IsHighSurrogate(text[i]))
			{
				if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return true;
				i++;
			}
			else if (char.IsLowSurrogate(text[i]))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Escapes a counterexample so the failure message survives a terminal.</summary>
	private static string Show(string text)
	{
		var escaped = new StringBuilder(text.Length + 2).Append('"');
		foreach (var c in text)
		{
			if (c is '\\' or '"') escaped.Append('\\').Append(c);
			else if (c is >= ' ' and <= '~') escaped.Append(c);
			else escaped.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
		}

		return escaped.Append('"').ToString();
	}
}

/// <summary>The violation a property in <see cref="RoundTripProperties"/> throws; CsCheck shrinks on it.</summary>
internal sealed class MarkupPropertyException(string message) : Exception(message);
