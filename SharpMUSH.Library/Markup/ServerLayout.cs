using MarkupString;
using System.Collections.Immutable;
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

	/// <summary>
	/// The frame round a server listing: rounded lines to a client that reads UTF-8, <c>+-+</c> to one that
	/// does not, and a titled panel in the portal.
	/// </summary>
	public static BorderStyle PanelBorder => BorderStyle.Rounded;

	/// <summary>The line under a listing's headings; an ASCII-only client gets <c>-</c>.</summary>
	public static MarkupText HeadingRule { get; } = MarkupText.Plain("─");

	/// <summary>
	/// <paramref name="parts"/> in a <see cref="PanelBorder"/> frame titled <paramref name="title"/>. A
	/// <see cref="Rule"/> among the parts is drawn as a divider meeting the frame's sides.
	/// </summary>
	public static Block Panel(MarkupText title, params ReadOnlySpan<Block> parts) =>
		new Frame(parts.Length == 1 ? parts[0] : new Stack([.. parts])) { Border = PanelBorder, Title = title };

	/// <summary>A table with the house heading rule and two cells between columns.</summary>
	public static Table Listing(ImmutableArray<TableColumn> columns, IEnumerable<ImmutableArray<Block>> rows) =>
		new(columns, [.. rows]) { HeaderRule = HeadingRule };

	/// <summary>A table of plain-text rows with the house heading rule.</summary>
	public static Table Listing(ImmutableArray<TableColumn> columns, IEnumerable<IEnumerable<string>> rows) =>
		Listing(columns, rows.Select(row => row.Select(cell => (Block)new TextBlock(MarkupText.Plain(cell))).ToImmutableArray()));

	/// <summary>Labelled values, <c>Label: value</c>, the values lined up in one column.</summary>
	public static Fields KeyValues(IEnumerable<(string Label, MarkupText Value)> items) =>
		new([.. items.Select(item => new Field(MarkupText.Plain(item.Label), Body(item.Value)))]);

	/// <summary>Text as one block: the layouts in it kept as layouts, the text between them as text.</summary>
	public static Block Body(MarkupText text)
	{
		var blocks = BlockLayout.Blocks(text);
		return blocks.Count switch
		{
			0 => new TextBlock(MarkupText.Empty),
			1 => blocks[0],
			_ => new Stack([.. blocks]),
		};
	}

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
