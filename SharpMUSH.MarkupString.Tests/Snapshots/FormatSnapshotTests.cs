using System.Globalization;
using System.Text;
using MarkupString.Ansi;
using MarkupString.Html;
namespace SharpMUSH.MarkupString.Tests.Snapshots;

/// <summary>
/// Renders one fixture set through every built-in format and pins the whole table with a snapshot,
/// so a change to any emitter shows up as a reviewable diff of every case at once rather than as a
/// scatter of individual assertion failures.
/// </summary>
/// <remarks>
/// Control characters are written as <c>&lt;ESC&gt;</c> and <c>&lt;BEL&gt;</c> in the snapshot: the
/// ANSI stream is otherwise unreadable in a diff, and a literal escape in a checked-in file is at
/// the mercy of editors and terminals. Everything else is verbatim emitter output.
/// </remarks>
public class FormatSnapshotTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));
	private static readonly AnsiMarkup Green = AnsiMarkup.Create(foreground: new AnsiColor.Standard(2, false));
	private static readonly AnsiMarkup Bold = AnsiMarkup.Create(bold: true);
	private static readonly AnsiMarkup Underlined = AnsiMarkup.Create(underlined: true);

	/// <summary>
	/// The cases every format is rendered over. Order is fixed: the snapshot is keyed on it, so new
	/// fixtures go at the end and existing ones are never reordered or renamed lightly.
	/// </summary>
	private static readonly (string Name, MarkupText Text)[] Fixtures =
	[
		("plain", MarkupText.Plain("plain text")),

		("bold", MarkupText.Wrap(Bold, "bold")),
		("faint", MarkupText.Wrap(AnsiMarkup.Create(faint: true), "faint")),
		("italic", MarkupText.Wrap(AnsiMarkup.Create(italic: true), "italic")),
		("underlined", MarkupText.Wrap(Underlined, "underlined")),
		("overlined", MarkupText.Wrap(AnsiMarkup.Create(overlined: true), "overlined")),
		("blink", MarkupText.Wrap(AnsiMarkup.Create(blink: true), "blink")),
		("inverted", MarkupText.Wrap(AnsiMarkup.Create(inverted: true), "inverted")),
		("strikethrough", MarkupText.Wrap(AnsiMarkup.Create(strikeThrough: true), "strikethrough")),

		("fg-standard", MarkupText.Wrap(Red, "red")),
		("fg-bright", MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, true)), "bright red")),
		("fg-xterm-200", MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(200)), "xterm 200")),
		("fg-rgb", MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Rgb(0x12, 0x34, 0x56)), "rgb")),
		("fg-default", MarkupText.Wrap(AnsiMarkup.Create(foreground: AnsiColor.Default.Instance), "default fg")),
		("bg-standard", MarkupText.Wrap(AnsiMarkup.Create(background: new AnsiColor.Standard(4, false)), "on blue")),
		("bg-bright", MarkupText.Wrap(AnsiMarkup.Create(background: new AnsiColor.Standard(4, true)), "on bright blue")),
		("bg-xterm-24", MarkupText.Wrap(AnsiMarkup.Create(background: new AnsiColor.Xterm(24)), "on xterm 24")),
		("bg-rgb", MarkupText.Wrap(AnsiMarkup.Create(background: new AnsiColor.Rgb(0, 0x80, 0x40)), "on rgb")),
		("bg-default", MarkupText.Wrap(AnsiMarkup.Create(background: AnsiColor.Default.Instance), "default bg")),

		("link-url", MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "https://example.com/a?b=1&c=2"), "example")),
		("link-command", MarkupText.Wrap(
			AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who is on?"), "who")),

		("nested-three-deep", MarkupText.Wrap(Underlined, MarkupText.Wrap(Red, MarkupText.Wrap(Bold, "deep")))),
		("adjacent-runs", MarkupText.Concat(
		[
			MarkupText.Wrap(Red, "red"),
			MarkupText.Plain("-"),
			MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(2, false), bold: true), "bold green"),
			MarkupText.Wrap(Underlined, "under"),
		])),

		("html-send", MarkupText.Wrap(HtmlMarkup.Create("send", "href=\"north\" hint=\"Go north\""), "north")),
		("html-b-in-colour", MarkupText.Wrap(Green, MarkupText.Wrap(HtmlMarkup.Create("b"), "bold green"))),

		("unicode", MarkupText.Wrap(Red, "日本語 \U0001F600 café")),
		("needs-encoding", MarkupText.Wrap(Bold, "<a href=\"x\"> & 'y' </a>")),

		("clear-after-colour", MarkupText.Concat(
			MarkupText.Wrap(Red, "red"),
			MarkupText.Wrap(AnsiMarkup.Create(clear: true), "cleared"))),
	];

	[Test]
	[Arguments("plain")]
	[Arguments("ansi")]
	[Arguments("html")]
	[Arguments("pueblo")]
	[Arguments("mxp")]
	[Arguments("bbcode")]
	public async Task EveryFixture_RendersStably(string formatName)
	{
		var format = MarkupFormat.TryParse(formatName)
			?? throw new ArgumentException($"'{formatName}' is not a built-in format.", nameof(formatName));

		var width = Fixtures.Max(f => f.Name.Length);
		var table = new StringBuilder();
		for (var i = 0; i < Fixtures.Length; i++)
		{
			var (name, text) = Fixtures[i];
			// '\n' rather than AppendLine: the snapshot is a checked-in file compared byte for byte,
			// and Environment.NewLine would make it depend on the machine that ran the test.
			table.Append((i + 1).ToString("00", CultureInfo.InvariantCulture)).Append(' ').Append(name.PadRight(width))
				.Append(" | ").Append(Visible(text.Render(format, Registry))).Append('\n');
		}

		await Verify(table.ToString()).UseParameters(formatName);
	}

	/// <summary>Makes the control characters in an ANSI stream legible in a checked-in snapshot.</summary>
	private static string Visible(string rendered) =>
		rendered.Replace("\u001b", "<ESC>", StringComparison.Ordinal)
			.Replace("\u0007", "<BEL>", StringComparison.Ordinal);
}
