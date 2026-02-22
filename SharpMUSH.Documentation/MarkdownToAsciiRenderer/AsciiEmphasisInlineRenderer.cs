using ANSILibrary;
using Markdig.Syntax.Inlines;
using System.Drawing;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiEmphasisInlineRenderer : AsciiObjectRenderer<EmphasisInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, EmphasisInline obj)
	{
		var ansiStyle = MModule.markupSingle(Ansi.Create(foreground: StringExtensions.rgb(Color.White), bold: true), string.Empty);
		renderer.Write(ansiStyle);
		renderer.WriteChildren(obj);
	}
}