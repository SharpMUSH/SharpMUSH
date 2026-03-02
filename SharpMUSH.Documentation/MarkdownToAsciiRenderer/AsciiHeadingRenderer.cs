using ANSILibrary;
using Markdig.Syntax;
using System.Drawing;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHeadingRenderer : AsciiObjectRenderer<HeadingBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, HeadingBlock obj)
	{
		var ansiStyle = obj.Level switch
		{
			1 or 2 => MModule.markupSingle(Ansi.Create(foreground: StringExtensions.rgb(Color.White), underlined: true, bold: true), string.Empty),
			3 => MModule.markupSingle(Ansi.Create(foreground: StringExtensions.rgb(Color.White), underlined: true), string.Empty),
			_ => MModule.single(string.Empty)
		};

		renderer.Write(ansiStyle);
		renderer.WriteLeafInline(obj);
		renderer.EnsureLine();
	}
}