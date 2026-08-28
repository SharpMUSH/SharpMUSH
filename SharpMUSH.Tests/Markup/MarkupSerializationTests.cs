using ANSILibrary;
using MarkupString.MarkupImplementation;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using System.Collections.Immutable;
using System.Drawing;
using System.Text;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using H = MarkupString.MarkupImplementation.HtmlMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// The compact serialization format: <c>{"t":text,"p":[palette],"r":[len,idx,…]}</c>.
/// </summary>
/// <remarks>
/// Palette index 0 is always <c>null</c>, meaning "no markup", and <c>r</c> is a complete cover of
/// <c>t</c> so run starts are the running sum rather than stored. Both <c>p</c> and <c>r</c> are
/// omitted when a string carries no markup, which is the common case for a stored attribute.
/// </remarks>
public class MarkupSerializationTests
{
	// ── Shape ────────────────────────────────────────────────────────────────────

	[Test]
	public async Task PlainString_SerializesToTextOnly()
	{
		await Assert.That(A.serialize(A.single("hello"))).IsEqualTo("""{"t":"hello"}""");
	}

	[Test]
	public async Task EmptyString_SerializesToAnEmptyObject()
	{
		await Assert.That(A.serialize(A.empty())).IsEqualTo("{}");
	}

	[Test]
	public async Task SingleMarkedString_SerializesWithAPaletteAndACover()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));

		await Assert.That(A.serialize(A.MarkupSingle(red, "hello")))
			.IsEqualTo("""{"t":"hello","p":[null,[{"f":"#ff0000"}]],"r":[5,1]}""");
	}

	[Test]
	public async Task UnmarkedLeadingText_UsesPaletteIndexZero()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var combined = A.concat(A.single("Hello "), A.MarkupSingle(red, "World"));

		await Assert.That(A.serialize(combined))
			.IsEqualTo("""{"t":"Hello World","p":[null,[{"f":"#ff0000"}]],"r":[6,0,5,1]}""");
	}

	/// <summary>
	/// The point of the palette: a value used by many runs is written once. Twenty alternating
	/// fragments carry two distinct markups, so the palette has two entries and not twenty.
	/// </summary>
	[Test]
	public async Task RepeatedMarkup_IsWrittenOncePerDistinctValue()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blue = M.Create(foreground: new AnsiColor.RGB(Color.Blue));

		var alternating = A.multiple(Enumerable.Range(0, 20)
			.Select(i => A.MarkupSingle(i % 2 == 0 ? red : blue, i.ToString()))
			.ToArray());

		var json = A.serialize(alternating);

		await Assert.That(CountOccurrences(json, "#ff0000")).IsEqualTo(1);
		await Assert.That(CountOccurrences(json, "#0000ff")).IsEqualTo(1);
		await Assert.That(A.deserialize(json).ToPlainText()).IsEqualTo(alternating.ToPlainText());
	}

	// ── Round-trips ──────────────────────────────────────────────────────────────

	[Test]
	[MethodDataSource(nameof(RoundTripCases))]
	public async Task RoundTrip_PreservesTextRunsAndRenders(string name, MString original)
	{
		var restored = A.deserialize(A.serialize(original));

		await Assert.That(restored.ToPlainText()).IsEqualTo(original.ToPlainText()).Because(name);
		await Assert.That(restored.Runs.Length).IsEqualTo(original.Runs.Length).Because(name);
		await Assert.That(restored.Render("ansi")).IsEqualTo(original.Render("ansi")).Because(name);
		await Assert.That(restored.Render("html")).IsEqualTo(original.Render("html")).Because(name);
		await Assert.That(restored.Render("pueblo")).IsEqualTo(original.Render("pueblo")).Because(name);
		await Assert.That(restored.Render("mxp")).IsEqualTo(original.Render("mxp")).Because(name);
	}

	public static IEnumerable<Func<(string, MString)>> RoundTripCases()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blue = M.Create(foreground: new AnsiColor.RGB(Color.Blue), bold: true);
		var everything = M.Create(
			foreground: new AnsiColor.RGB(Color.Chartreuse),
			background: new AnsiColor.RGB(Color.DarkSlateGray),
			linkText: "look here", linkUrl: "look", linkKind: LinkKind.Command,
			blink: true, bold: true, clear: true, faint: true, inverted: true,
			italic: true, overlined: true, underlined: true, strikeThrough: true);
		var byteColor = M.Create(foreground: new AnsiColor.ANSI([1, 31]));
		var bold = H.Create("b");
		var send = H.Create("send", "href=look");

		yield return () => ("empty", A.empty());
		yield return () => ("plain", A.single("Hello, World!"));
		yield return () => ("single markup", A.MarkupSingle(red, "Red"));
		yield return () => ("every ansi attribute set", A.MarkupSingle(everything, "Loud"));
		yield return () => ("raw ansi byte colour", A.MarkupSingle(byteColor, "Bytes"));
		yield return () => ("html tag", A.MarkupSingle(bold, "Bold"));
		yield return () => ("html tag with attributes", A.MarkupSingle(send, "Look"));
		yield return () => ("mixed plain and marked",
			A.concat(A.single("Plain "), A.MarkupSingle(red, "Red")));
		yield return () => ("three differing runs", A.multiple(
			[A.MarkupSingle(red, "R"), A.MarkupSingle(blue, "B"), A.single("plain")]));
		yield return () => ("stacked markups on one run",
			A.MarkupSingleMulti(ImmutableArray.Create<IMarkup>(red, bold), "Both"));
		yield return () => ("markup wrapping an empty string", A.MarkupSingle2(red, A.empty()));
		yield return () => ("text with json metacharacters",
			A.MarkupSingle(red, "quote \" backslash \\ brace } newline \n tab \t"));
		yield return () => ("non-ascii text", A.MarkupSingle(red, "日本語 — ünïcodé — 🎲"));
	}

	/// <summary>
	/// A zero-length run carrying markup is how <c>MarkupSingle2</c> represents a styled empty
	/// string. The cover encodes it as a length of 0, which the reader must not treat as a
	/// terminator.
	/// </summary>
	[Test]
	public async Task ZeroLengthMarkedRun_RoundTrips()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));

		var restored = A.deserialize(A.serialize(A.MarkupSingle2(red, A.empty())));

		await Assert.That(restored.ToPlainText()).IsEqualTo("");
		await Assert.That(restored.Runs.Length).IsEqualTo(1);
		await Assert.That(restored.Runs[0].Length).IsEqualTo(0);
		await Assert.That(restored.Runs[0].Markups.Length).IsEqualTo(1);
	}

	[Test]
	public async Task EmptyInput_DeserializesToEmpty()
	{
		await Assert.That(A.deserialize("").ToPlainText()).IsEqualTo("");
		await Assert.That(A.deserialize("{}").ToPlainText()).IsEqualTo("");
	}

	// ── Size ─────────────────────────────────────────────────────────────────────

	/// <summary>
	/// The case that started this: <c>@wiki help:general:markdown_guide</c> serialised to 1,521,690
	/// bytes and exceeded the NATS payload limit. The seeded guide measured 794,777 bytes through the
	/// old format at 2,440 runs. Coalescing takes it to 37,589; the compact format takes it under
	/// 10,000.
	/// </summary>
	[Test]
	public async Task RenderedWikiGuide_SerializesUnderTenKilobytes()
	{
		var rendered = RecursiveMarkdownHelper.RenderMarkdown(MarkdownGuideExcerpt, 78);

		var bytes = Encoding.UTF8.GetByteCount(A.serialize(rendered));

		await Assert.That(rendered.Runs.Length).IsLessThan(400);
		await Assert.That(bytes).IsLessThan(10_000);
		await Assert.That(A.deserialize(A.serialize(rendered)).Render("ansi"))
			.IsEqualTo(rendered.Render("ansi"));
	}

	[Test]
	public async Task PlainAttribute_CostsLittleMoreThanItsText()
	{
		// The common stored attribute: no markup at all. The old format spent 80 bytes on 5
		// characters; the compact one spends 13.
		await Assert.That(Encoding.UTF8.GetByteCount(A.serialize(A.single("hello")))).IsLessThan(20);
	}

	private static int CountOccurrences(string haystack, string needle)
	{
		var count = 0;
		for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
				 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			count++;
		return count;
	}

	/// <summary>
	/// A slice of the seeded Help:Markdown Guide, chosen for the features that generate runs:
	/// a table, fenced code blocks with language tags (which the syntax highlighter colours one
	/// token at a time), inline emphasis, and links.
	/// </summary>
	private const string MarkdownGuideExcerpt = """
		The wiki uses **CommonMark** Markdown with the extensions described below.
		Raw HTML is **disabled** for security.

		## Basic formatting

		| You type | You get |
		|---|---|
		| `**bold**` | **bold** |
		| `_italic_` | _italic_ |
		| `~~strikethrough~~` | ~~strikethrough~~ |
		| `# Heading` … `###### Heading` | section headings |
		| `> quoted text` | a blockquote |

		## Lists

		```json
		{"a": 1, "b": [2, 3], "c": {"nested": true}}
		```

		```csharp
		public static string Wrap(string text) => $"<b>{text}</b>";
		```

		## Links

		- External: `[link text](https://example.com)`
		- **Wiki links**: `[[Page Name]]` links to a page in this wiki.
		- Custom text: `[[Display Text|Page Name]]`
		- Bare URLs like https://example.com auto-link.
		""";
}
