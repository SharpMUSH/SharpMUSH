using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHeadingRenderer : AsciiObjectRenderer<HeadingBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, HeadingBlock obj)
	{
		var contents = obj.ToString() ?? string.Empty;
		var rendered = obj.Level switch
		{
			1 or 2 => MModule.markupSingle(Ansi.Create(underlined: true, bold: true), "<HEADER STYLE>"),
			3 => MModule.markupSingle(Ansi.Create(underlined: true), "<HEADER STYLE>"),
			_ => MModule.single("<HEADER STYLE>")
		};

		renderer.Write(rendered);
		renderer.WriteLeafInline(obj);

		renderer.EnsureLine();
	}
}