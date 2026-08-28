using ANSILibrary;
using MarkupString.MarkupImplementation;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MarkupString;

/// <summary>
/// The wire and storage format for a <see cref="MarkupString"/>.
/// </summary>
/// <remarks>
/// <code>
/// {"t":"…","p":[null,[{"f":"#ffffff","bo":1}],[{"f":"#569cd6"}]],"r":[12,0,8,1,40,0]}
/// </code>
/// <list type="bullet">
///   <item><c>t</c> — the plain text, omitted when empty.</item>
///   <item><c>p</c> — the palette of distinct markup values. Index 0 is always <c>null</c>, meaning
///   "no markup". Entries are arrays even in the overwhelmingly common single-markup case: two bytes
///   per palette entry buys a reader with no union type in it, and a palette holds one entry per
///   *distinct* value, so it stays tiny.</item>
///   <item><c>r</c> — a flat <c>[length, paletteIndex, …]</c> cover of <c>t</c>. Run starts are the
///   running sum, so no start, end, or total length is stored.</item>
/// </list>
/// <c>p</c> and <c>r</c> are both omitted when nothing carries markup, so a plain attribute — the
/// common case in a game database — costs its text plus eight bytes.
/// <para>
/// The shape is deliberately not a mirror of the object model, which is why this is hand-written
/// rather than attributes on a DTO: the palette has to be built by walking the runs, and the cover
/// has to fill the gaps between them.
/// </para>
/// </remarks>
internal static class MarkupStringSerializer
{
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

	public static string Serialize(MarkupString ams)
	{
		var buffer = new ArrayBufferWriter<byte>(ams.Text.Length + 64);
		using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
		{
			Write(writer, ams);
		}
		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	private static void Write(Utf8JsonWriter writer, MarkupString ams)
	{
		var text = ams.Text;
		var runs = ams.Runs;

		// Index 0 is the reserved "no markup" slot; distinct markup lists take 1..n.
		var palette = new List<ImmutableArray<IMarkup>>();
		var indexOf = new Dictionary<ImmutableArray<IMarkup>, int>(MarkupListComparer.Instance);
		var cover = new List<int>(runs.Length * 2 + 2);

		var position = 0;
		foreach (var run in runs)
		{
			if (run.Markups.Length == 0)
			{
				// An unmarked run is indistinguishable from a gap; both become palette index 0, and
				// adjacent stretches of it are folded together.
				Cover(cover, run.End - position, 0);
				position = run.End;
				continue;
			}

			if (run.Start > position) Cover(cover, run.Start - position, 0);

			if (!indexOf.TryGetValue(run.Markups, out var index))
			{
				palette.Add(run.Markups);
				index = palette.Count; // 1-based: slot 0 is the null entry.
				indexOf[run.Markups] = index;
			}

			// Length may legitimately be 0 — MarkupSingle2 marks an empty string that way — so this
			// entry is written unconditionally rather than being folded away as a no-op.
			cover.Add(run.Length);
			cover.Add(index);
			position = run.End;
		}

		writer.WriteStartObject();

		if (text.Length > 0) writer.WriteString("t", text);

		if (palette.Count > 0)
		{
			if (position < text.Length) Cover(cover, text.Length - position, 0);

			writer.WriteStartArray("p");
			writer.WriteNullValue();
			foreach (var markups in palette)
			{
				writer.WriteStartArray();
				foreach (var markup in markups) WriteMarkup(writer, markup);
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

	private static void WriteMarkup(Utf8JsonWriter writer, IMarkup markup)
	{
		writer.WriteStartObject();
		switch (markup)
		{
			case AnsiMarkup ansi:
				WriteAnsi(writer, ansi.Details);
				break;
			case HtmlMarkup html:
				// The presence of "h" is the discriminator, so no type tag is written for any variant.
				writer.WriteString("h", html.Details.TagName);
				if (html.Details.Attributes is { Length: > 0 } attributes)
					writer.WriteString("a", attributes);
				break;
			default:
				writer.WriteNumber("n", 1);
				break;
		}
		writer.WriteEndObject();
	}

	private static void WriteAnsi(Utf8JsonWriter writer, AnsiStructure d)
	{
		WriteColor(writer, "f", d.Foreground);
		WriteColor(writer, "g", d.Background);
		if (d.LinkText is { Length: > 0 }) writer.WriteString("lt", d.LinkText);
		if (d.LinkUrl is { Length: > 0 }) writer.WriteString("lu", d.LinkUrl);
		if (d.LinkKind != LinkKind.Url) writer.WriteNumber("lk", (int)d.LinkKind);
		if (d.Blink) writer.WriteNumber("bl", 1);
		if (d.Bold) writer.WriteNumber("bo", 1);
		if (d.Clear) writer.WriteNumber("cl", 1);
		if (d.Faint) writer.WriteNumber("fa", 1);
		if (d.Inverted) writer.WriteNumber("in", 1);
		if (d.Italic) writer.WriteNumber("it", 1);
		if (d.Overlined) writer.WriteNumber("ov", 1);
		if (d.Underlined) writer.WriteNumber("un", 1);
		if (d.StrikeThrough) writer.WriteNumber("st", 1);
	}

	/// <summary>
	/// RGB becomes <c>#rrggbb</c>, or <c>#rrggbbaa</c> when not fully opaque. A raw SGR sequence
	/// becomes an array of its bytes. <see cref="AnsiColor.NoAnsi"/> is the default and is omitted.
	/// </summary>
	private static void WriteColor(Utf8JsonWriter writer, string name, AnsiColor? color)
	{
		switch (color)
		{
			case AnsiColor.RGB { Value: var c }:
				writer.WriteString(name, c.A == 255
					? $"#{c.R:x2}{c.G:x2}{c.B:x2}"
					: $"#{c.R:x2}{c.G:x2}{c.B:x2}{c.A:x2}");
				break;
			case AnsiColor.ANSI { Value: var bytes }:
				writer.WriteStartArray(name);
				foreach (var b in bytes ?? []) writer.WriteNumberValue(b);
				writer.WriteEndArray();
				break;
		}
	}

	// ── Reading ──────────────────────────────────────────────────────────────────

	public static MarkupString Deserialize(string json)
	{
		if (json.Length == 0) return MarkupStringModule.Empty();

		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;

		var text = root.TryGetProperty("t", out var textElement)
			? textElement.GetString() ?? string.Empty
			: string.Empty;

		if (!root.TryGetProperty("p", out var paletteElement)
				|| !root.TryGetProperty("r", out var coverElement))
			return MarkupStringModule.Single(text);

		var palette = ReadPalette(paletteElement);
		var runs = ImmutableArray.CreateBuilder<AttributeRun>();
		var position = 0;

		using var cover = coverElement.EnumerateArray().GetEnumerator();
		while (cover.MoveNext())
		{
			var length = cover.Current.GetInt32();
			if (!cover.MoveNext()) break; // Truncated pair: stop rather than reading a length as an index.
			var index = cover.Current.GetInt32();

			// Slot 0 is the reserved "no markup" entry. It still becomes a run: RenderWith walks the
			// runs and emits only the text they cover, so text left uncovered would vanish from every
			// render.
			var markups = index > 0 && index <= palette.Length
				? palette[index - 1]
				: ImmutableArray<IMarkup>.Empty;

			runs.Add(new AttributeRun(position, length, markups));
			position += length;
		}

		return new MarkupString(text, runs.ToImmutable());
	}

	private static ImmutableArray<IMarkup>[] ReadPalette(JsonElement palette)
	{
		// Slot 0 is the reserved null entry and is not represented here; callers subtract one.
		var entries = new List<ImmutableArray<IMarkup>>();
		var first = true;
		foreach (var entry in palette.EnumerateArray())
		{
			if (first)
			{
				first = false;
				continue;
			}

			var markups = ImmutableArray.CreateBuilder<IMarkup>();
			foreach (var markup in entry.EnumerateArray()) markups.Add(ReadMarkup(markup));
			entries.Add(markups.ToImmutable());
		}
		return [.. entries];
	}

	private static IMarkup ReadMarkup(JsonElement element)
	{
		if (element.TryGetProperty("h", out var tag))
			return HtmlMarkup.Create(
				tag.GetString() ?? string.Empty,
				element.TryGetProperty("a", out var attributes) ? attributes.GetString() : null);

		if (element.TryGetProperty("n", out _)) return NeutralMarkup.Instance;

		return new AnsiMarkup(new AnsiStructure
		{
			Foreground = ReadColor(element, "f"),
			Background = ReadColor(element, "g"),
			LinkText = ReadString(element, "lt"),
			LinkUrl = ReadString(element, "lu"),
			LinkKind = element.TryGetProperty("lk", out var kind) ? (LinkKind)kind.GetInt32() : LinkKind.Url,
			Blink = element.TryGetProperty("bl", out _),
			Bold = element.TryGetProperty("bo", out _),
			Clear = element.TryGetProperty("cl", out _),
			Faint = element.TryGetProperty("fa", out _),
			Inverted = element.TryGetProperty("in", out _),
			Italic = element.TryGetProperty("it", out _),
			Overlined = element.TryGetProperty("ov", out _),
			Underlined = element.TryGetProperty("un", out _),
			StrikeThrough = element.TryGetProperty("st", out _),
		});
	}

	private static string ReadString(JsonElement element, string name) =>
			element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

	private static AnsiColor ReadColor(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value)) return AnsiColor.NoAnsi.Instance;

		if (value.ValueKind == JsonValueKind.Array)
		{
			var bytes = new List<byte>();
			foreach (var b in value.EnumerateArray()) bytes.Add(b.GetByte());
			return new AnsiColor.ANSI([.. bytes]);
		}

		var hex = value.GetString();
		if (hex is not { Length: 7 or 9 } || hex[0] != '#') return AnsiColor.NoAnsi.Instance;

		var r = byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		var g = byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		var b2 = byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		var a = hex.Length == 9
			? byte.Parse(hex.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
			: (byte)255;

		return new AnsiColor.RGB(Color.FromArgb(a, r, g, b2));
	}

	/// <summary>
	/// Compares markup lists by value so the palette holds one entry per distinct value rather than
	/// one per instance. Relies on the value equality <see cref="AnsiMarkup"/> and
	/// <see cref="HtmlMarkup"/> forward to their details structs.
	/// </summary>
	private sealed class MarkupListComparer : IEqualityComparer<ImmutableArray<IMarkup>>
	{
		public static readonly MarkupListComparer Instance = new();

		public bool Equals(ImmutableArray<IMarkup> x, ImmutableArray<IMarkup> y)
		{
			if (x.Length != y.Length) return false;
			for (var i = 0; i < x.Length; i++)
				if (!x[i].Equals(y[i]))
					return false;
			return true;
		}

		public int GetHashCode(ImmutableArray<IMarkup> obj)
		{
			var hash = new HashCode();
			foreach (var markup in obj) hash.Add(markup);
			return hash.ToHashCode();
		}
	}
}
