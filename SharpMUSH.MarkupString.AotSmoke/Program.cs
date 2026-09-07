using System.Buffers;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

// Native-AOT smoke test. It is published with PublishAot and run in CI: the publish must produce no
// IL2xxx/IL3xxx warning, and the binary must exit 0. What it exercises is the whole pipeline that a
// consumer touches — registry composition, nested markup, every built-in format, and the JSON
// round trip — because those are where reflection or dynamic code would have crept in.

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();

// Bold red on its own, so the plain SGR sequence appears un-merged with anything else.
var red = MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "red");

// A tag layer over a style layer over a colour layer: three kinds nested, folded differently per
// format — one <send> element in HTML, one SGR sequence in the terminal.
var nested = MarkupText.Wrap(
	HtmlMarkup.Create("send", "href=\"n\""),
	MarkupText.Wrap(
		AnsiMarkup.Create(underlined: true),
		MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "north")));

// Wide CJK and a multi-codepoint emoji: grapheme and display-width handling without ICU, since
// this app sets InvariantGlobalization.
var wide = MarkupText.Plain(" 日本語テキスト 👩‍👩‍👧‍👦 ");

var link = MarkupText.Wrap(
	AnsiMarkup.Create(linkUrl: "https://example.com/", linkText: "example", linkKind: LinkKind.Url),
	"example");

// A command link, which is the one thing Pueblo and MXP spell differently (XCH_CMD against SEND):
// without it every assertion those two formats could make is one the other satisfies too.
var command = MarkupText.Wrap(
	AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command),
	"look");

var text = MarkupText.Concat([red, wide, nested, MarkupText.Space, link, MarkupText.Space, command]);

MarkupFormat[] formats =
[
	MarkupFormat.Plain,
	MarkupFormat.Ansi,
	MarkupFormat.Html,
	MarkupFormat.Pueblo,
	MarkupFormat.Mxp,
	MarkupFormat.BBCode
];

var failures = new List<string>();

var rendered = new Dictionary<string, string>();
foreach (var format in formats)
{
	rendered[format.Name] = text.Render(format);
}

// Both serialiser paths: the string one and the UTF-8 buffer one, read back through both readers.
var json = MarkupTextSerializer.Serialize(text);
var buffer = new ArrayBufferWriter<byte>();
MarkupTextSerializer.Serialize(text, buffer);

foreach (var (label, roundTripped) in new[]
{
	("string", MarkupTextSerializer.Deserialize(json)),
	("utf8", MarkupTextSerializer.Deserialize(buffer.WrittenSpan))
})
{
	foreach (var format in formats)
	{
		var after = roundTripped.Render(format);
		if (!string.Equals(after, rendered[format.Name], StringComparison.Ordinal))
		{
			failures.Add($"{format.Name} differs after the {label} round trip:\n  before: {Escape(rendered[format.Name])}\n  after:  {Escape(after)}");
		}
	}
}

// The renders have to contain the real thing, not merely be stable: a registry that silently
// dropped every emitter would round-trip perfectly and render nothing. One substring per format,
// so a format whose emitter silently regressed to a neighbour's output (e.g. Mxp falling back to
// Pueblo's escape-code path) still trips a distinct check.
Expect(rendered["plain"], "日本語テキスト", "plain");
Expect(rendered["ansi"], "\e[1;31m", "ansi");
Expect(rendered["html"], "<send href=\"n\">", "html");
Expect(rendered["html"], "color: #ff5555", "html");
Expect(rendered["pueblo"], "<A XCH_CMD=\"look\"", "pueblo");
Expect(rendered["mxp"], "<SEND HREF=\"look\"", "mxp");
Expect(rendered["bbcode"], "[color=#ff5555]", "bbcode");

if (failures.Count > 0)
{
	Console.Error.WriteLine("AOT smoke test failed:");
	foreach (var failure in failures)
	{
		Console.Error.WriteLine($"  - {failure}");
	}

	return 1;
}

Console.WriteLine("ok");
return 0;

void Expect(string actual, string expected, string format)
{
	if (!actual.Contains(expected, StringComparison.Ordinal))
	{
		failures.Add($"the {format} render is missing {Escape(expected)}: {Escape(actual)}");
	}
}

static string Escape(string value) => value.Replace("\e", "\\e", StringComparison.Ordinal);
