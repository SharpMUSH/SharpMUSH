using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHeadingRenderer : AsciiObjectRenderer<HeadingBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, HeadingBlock obj)
	{
		var contents = obj.ToString() ?? string.Empty;
		var rendered = obj.Level switch
		{
			1 or 2 => MModule.markupSingle(Ansi.Create(underlined: true, bold: true), contents.ToUpper()),
			3 => MModule.markupSingle(Ansi.Create(underlined: true), contents),
			_ => MModule.single(contents)
		};

		renderer.WriteLine(rendered.ToString());
	}
}