using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
namespace MarkupString;

/// <summary>
/// The wire and storage format for a <see cref="MarkupText"/>.
/// </summary>
/// <remarks>
/// <code>
/// {"t":"…","p":[null,[{"k":"ansi","f":"#ffffff","bo":1}],[{"k":"ansi","f":"#569cd6"}]],"r":[12,0,8,1,40,0]}
/// </code>
/// <list type="bullet">
///   <item><c>t</c> — the plain text, omitted when empty.</item>
///   <item><c>p</c> — the palette of distinct <see cref="MarkupSet"/>s. Index 0 is always
///   <c>null</c>, meaning "no markup". Every other entry is an array of markup objects written
///   innermost first, matching <see cref="MarkupSet"/> order. Entries are arrays even in the
///   overwhelmingly common single-markup case: two bytes per palette entry buys a reader with no
///   union type in it, and a palette holds one entry per <em>distinct</em> set, so it stays tiny.</item>
///   <item><c>r</c> — a flat <c>[length, paletteIndex, …]</c> cover of <c>t</c>. Run starts are the
///   running sum, so no start, end, or total length is stored. Gaps between runs are plain text and
///   take slot 0; adjacent slot-0 stretches fold into one entry.</item>
/// </list>
/// <c>p</c> and <c>r</c> are both omitted when nothing carries markup, so a plain attribute — the
/// common case in a game database — costs its text plus eight bytes.
/// <para>
/// Each markup object carries a <c>"k"</c> kind discriminator, written first by this class; a
/// registered <see cref="IMarkupCodec"/> writes and reads only its own properties. <c>"neutral"</c>
/// is built in and needs no codec. Objects written before <c>"k"</c> existed are still read: <c>"h"</c>
/// present means html, <c>"n"</c> present means neutral, and anything else is ansi. A kind no codec
/// claims becomes an <see cref="UnknownMarkup"/> holding the raw object, which is written back
/// verbatim, so a reader missing a package neither loses nor corrupts its markup.
/// </para>
/// <para>
/// The shape is deliberately not a mirror of the object model, which is why this is hand-written
/// rather than attributes on a DTO: the palette has to be built by walking the runs, and the cover
/// has to fill the gaps between them.
/// </para>
/// </remarks>
public static class MarkupTextSerializer
{
	/// <summary>The kind written for, and read back as, <see cref="NeutralMarkup.Instance"/>.</summary>
	private const string NeutralKind = "neutral";

	/// <summary>
	/// Leaves non-ASCII text as literal UTF-8 rather than <c>\uXXXX</c> escapes. The default encoder
	/// triples the cost of CJK and Cyrillic text, which several games are written in. "Unsafe" here
	/// means "not pre-escaped for embedding in HTML or script"; this output goes to a database, to
	/// NATS, and to a JSON parser, never into a document.
	/// </summary>
	private static readonly JsonWriterOptions WriterOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Indented = false,
	};

	// ── Writing ──────────────────────────────────────────────────────────────────

	/// <summary>Serialises <paramref name="text"/> to a JSON string.</summary>
	/// <param name="text">The text to serialise.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	/// <exception cref="InvalidOperationException">A markup has no codec in the registry.</exception>
	public static string Serialize(MarkupText text, MarkupRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(text);
		var buffer = new ArrayBufferWriter<byte>(text.Length + 64);
		Serialize(text, buffer, registry);
		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	/// <summary>Serialises <paramref name="text"/> as UTF-8 JSON into <paramref name="output"/>.</summary>
	/// <param name="text">The text to serialise.</param>
	/// <param name="output">The buffer the JSON is written to.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	/// <exception cref="InvalidOperationException">A markup has no codec in the registry.</exception>
	public static void Serialize(MarkupText text, IBufferWriter<byte> output, MarkupRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(output);
		using var writer = new Utf8JsonWriter(output, WriterOptions);
		Write(writer, text, registry);
	}

	private static void Write(Utf8JsonWriter writer, MarkupText text, MarkupRegistry? registry)
	{
		// Index 0 is the reserved "no markup" slot; distinct sets take 1..n.
		var palette = new List<MarkupSet>();
		var indexOf = new Dictionary<MarkupSet, int>();
		var cover = new List<int>(text.Runs.Length * 2 + 2);

		var position = 0;
		foreach (var run in text.Runs)
		{
			if (run.Start > position) Cover(cover, run.Start - position, 0);

			if (!indexOf.TryGetValue(run.Markups, out var index))
			{
				palette.Add(run.Markups);
				index = palette.Count; // 1-based: slot 0 is the null entry.
				indexOf[run.Markups] = index;
			}

			// Runs are never adjacent under an equal set (MarkupText coalesces them), so a run's entry
			// can never fold into the previous one and is appended directly.
			cover.Add(run.Length);
			cover.Add(index);
			position = run.End;
		}

		writer.WriteStartObject();

		if (text.Length > 0) writer.WriteString("t", text.Text);

		if (palette.Count > 0)
		{
			if (position < text.Length) Cover(cover, text.Length - position, 0);

			writer.WriteStartArray("p");
			writer.WriteNullValue();
			foreach (var markups in palette)
			{
				writer.WriteStartArray();
				foreach (var markup in markups) WriteMarkup(writer, markup, registry);
				writer.WriteEndArray();
			}
			writer.WriteEndArray();

			writer.WriteStartArray("r");
			foreach (var value in cover) writer.WriteNumberValue(value);
			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	/// <summary>Appends a cover entry, extending the previous one when it uses the same palette slot.</summary>
	private static void Cover(List<int> cover, int length, int index)
	{
		if (length <= 0) return;

		if (cover.Count >= 2 && cover[^1] == index)
			cover[^2] += length;
		else
		{
			cover.Add(length);
			cover.Add(index);
		}
	}

	/// <summary>
	/// Writes one markup layer. <paramref name="registry"/> falls back to
	/// <see cref="MarkupRegistry.Default"/> only where a codec is actually needed, so text carrying
	/// nothing but neutral or unknown layers serialises without one being configured.
	/// </summary>
	private static void WriteMarkup(Utf8JsonWriter writer, IMarkup markup, MarkupRegistry? registry)
	{
		// An unknown layer round-trips as the bytes it arrived as, kind discriminator and all. When
		// those bytes predate "k", the legacy inference on the next read lands on the same kind.
		if (markup is UnknownMarkup unknown)
		{
			writer.WriteRawValue(unknown.RawJson);
			return;
		}

		writer.WriteStartObject();
		if (markup is NeutralMarkup)
		{
			writer.WriteString("k", NeutralKind);
		}
		else
		{
			var codec = (registry ?? MarkupRegistry.Default).FindCodec(markup.GetType())
				?? throw new InvalidOperationException(
					$"No markup codec is registered for {markup.GetType()}. Add one with MarkupRegistry.With(IMarkupCodec).");
			writer.WriteString("k", codec.Kind);
			codec.Write(writer, markup);
		}
		writer.WriteEndObject();
	}

	// ── Reading ──────────────────────────────────────────────────────────────────

	/// <summary>Reads a <see cref="MarkupText"/> back from JSON. An empty string is <see cref="MarkupText.Empty"/>.</summary>
	/// <param name="json">The JSON produced by <see cref="Serialize(MarkupText, MarkupRegistry?)"/>.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	public static MarkupText Deserialize(string json, MarkupRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(json);
		if (json.Length == 0) return MarkupText.Empty;

		using var document = JsonDocument.Parse(json);
		return Read(document.RootElement, registry);
	}

	/// <summary>Reads a <see cref="MarkupText"/> back from UTF-8 JSON. An empty span is <see cref="MarkupText.Empty"/>.</summary>
	/// <param name="utf8Json">The UTF-8 JSON produced by <see cref="Serialize(MarkupText, IBufferWriter{byte}, MarkupRegistry?)"/>.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	/// <exception cref="JsonException">The payload has content after the JSON value.</exception>
	public static MarkupText Deserialize(ReadOnlySpan<byte> utf8Json, MarkupRegistry? registry = null)
	{
		if (utf8Json.IsEmpty) return MarkupText.Empty;

		var reader = new Utf8JsonReader(utf8Json);
		using var document = JsonDocument.ParseValue(ref reader);

		// ParseValue only consumes the one value; unlike JsonDocument.Parse(string), it does not by
		// itself reject trailing content. Draining the reader keeps both overloads agreeing on what
		// counts as valid input.
		if (reader.Read())
			throw new JsonException("Unexpected content after the JSON value.");

		return Read(document.RootElement, registry);
	}

	private static MarkupText Read(JsonElement root, MarkupRegistry? registry)
	{
		// Every shape check below treats a malformed payload as a less marked-up one rather than
		// throwing: this reads rows written by older builds and bytes off a bus, and losing colour on
		// one corrupt attribute beats failing the read that carries it.
		var text = root.TryGetProperty("t", out var textElement) && textElement.ValueKind == JsonValueKind.String
			? textElement.GetString() ?? string.Empty
			: string.Empty;

		if (!root.TryGetProperty("p", out var paletteElement) || paletteElement.ValueKind != JsonValueKind.Array
				|| !root.TryGetProperty("r", out var coverElement) || coverElement.ValueKind != JsonValueKind.Array)
			return MarkupText.Plain(text);

		var palette = ReadPalette(paletteElement, registry);
		var runs = ImmutableArray.CreateBuilder<Run>();
		var position = 0;

		using var cover = coverElement.EnumerateArray().GetEnumerator();
		while (cover.MoveNext())
		{
			// A non-numeric or negative length would desynchronise every later pair, so reading stops
			// there, as it does on a truncated pair rather than reading a length as an index.
			if (!TryReadInt32(cover.Current, out var length) || length < 0) break;
			if (!cover.MoveNext() || !TryReadInt32(cover.Current, out var index)) break;

			// A cover already at (or past) the end of the text has nothing left to describe, and an
			// unclipped length would let position overrun int range on the next add (two int.MaxValue
			// entries wrap it negative, which sorts runs out of order and trips the overlap check
			// below). Clipping here — rather than leaving it to the constructor — keeps position itself
			// bounded by text.Length across every iteration.
			if (position >= text.Length) break;
			var runLength = Math.Min(length, text.Length - position);

			// Slot 0 is the reserved "no markup" entry, and so is any index the palette does not have:
			// both leave the stretch as a gap, which is plain text.
			if (index > 0 && index <= palette.Length) runs.Add(new Run(position, runLength, palette[index - 1]));
			position += runLength;
		}

		return new MarkupText(text, runs.ToImmutable());
	}

	private static bool TryReadInt32(JsonElement element, out int value)
	{
		value = 0;
		return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
	}

	/// <summary>Reads the palette, skipping slot 0 (the reserved null entry); callers subtract one.</summary>
	private static MarkupSet[] ReadPalette(JsonElement palette, MarkupRegistry? registry)
	{
		var entries = new List<MarkupSet>();
		var markups = new List<IMarkup>();
		var first = true;
		foreach (var entry in palette.EnumerateArray())
		{
			if (first)
			{
				first = false;
				continue;
			}

			markups.Clear();
			if (entry.ValueKind == JsonValueKind.Array)
				foreach (var markup in entry.EnumerateArray())
					if (markup.ValueKind == JsonValueKind.Object)
						markups.Add(ReadMarkup(markup, registry));

			// A set must hold at least one markup; an entry that read as none is a slot no cover index
			// can usefully point at, so it takes a neutral layer rather than dropping and shifting
			// every later index.
			entries.Add(markups.Count == 0 ? MarkupSet.Of(NeutralMarkup.Instance) : MarkupSet.Of(markups));
		}
		return [.. entries];
	}

	/// <summary>
	/// Reads one markup layer. As on the write side, <paramref name="registry"/> falls back to
	/// <see cref="MarkupRegistry.Default"/> only where a codec is actually looked up.
	/// </summary>
	private static IMarkup ReadMarkup(JsonElement element, MarkupRegistry? registry)
	{
		var kind = element.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.String
			? k.GetString()!
			: element.TryGetProperty("h", out _) ? "html"
			: element.TryGetProperty("n", out _) ? NeutralKind
			: "ansi";

		if (kind == NeutralKind) return NeutralMarkup.Instance;

		var codec = (registry ?? MarkupRegistry.Default).FindCodec(kind);
		return codec is null ? new UnknownMarkup(kind, element.GetRawText()) : codec.Read(element);
	}
}
