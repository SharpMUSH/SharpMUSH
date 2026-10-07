using MarkupString;
using MarkupString.Layout;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// How the server's own output (listings, help, the Markdown renderer) lays a <see cref="Block"/> tree out
/// for a MUD client, on top of <see cref="BlockLayout.Build"/>.
/// </summary>
public static class ServerLayout
{
	/// <summary>
	/// PennMUSH's separator: a line of <c>-</c> with a title set into it as it is written, spaces and all. A
	/// rule drawn in it reads as PennMUSH's to every client, and is still a rule to the portal and to a
	/// client of another width.
	/// </summary>
	public static BorderStyle Dashes { get; } = BorderStyle.Ascii with { TitleOpen = MarkupText.Empty, TitleClose = MarkupText.Empty };

	/// <summary>A <see cref="Dashes"/> rule across <paramref name="width"/>, with <paramref name="title"/> centred in it.</summary>
	public static MarkupText DashedRule(int width, MarkupText? title = null) =>
		Build(new Rule(title) { Border = Dashes }, width);

	/// <summary>
	/// <paramref name="block"/> laid out at <paramref name="width"/> under a <see cref="LayoutMarkup"/>, so the
	/// rendering worker lays it out again for each client (its width when <paramref name="fluid"/>, ASCII
	/// borders without UTF-8, reading order for a screen reader) and the portal draws it as page structure.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The engine fills every line to the full width. A listing has no right edge to reach, so each line
	/// here ends at its last character instead: what a softcode caller measures and what a narrow client
	/// wraps is the text, not a run of spaces after it.
	/// </para>
	/// <para>
	/// The result is also marked preformatted: a Pueblo client has no layout of its own and reads the
	/// stream as HTML, where runs of spaces collapse. A format that draws the layout itself (the portal)
	/// takes the whole region and never sees that layer.
	/// </para>
	/// </remarks>
	public static MarkupText Build(Block block, int width, bool fluid = true)
	{
		ArgumentNullException.ThrowIfNull(block);
		var text = MarkupText.Join(MarkupText.NewLine,
			BlockLayout.Lines(block, width).Select(line => line.Trim(TrimType.TrimEnd, " ")));
		return text.Length == 0
			? text
			: MarkupText.Preformatted(MarkupText.Wrap(new LayoutMarkup(block, width, fluid, text.Text), text));
	}
}
