using System.Drawing;
using ANSILibrary;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiEmphasisInlineRenderer : AsciiObjectRenderer<EmphasisInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, EmphasisInline obj)
	{
		var ansi = MModule.markupSingle(Ansi.Create(foreground: StringExtensions.rgb(Color.White), bold: true), "<emphasis>");
		renderer.Write(ansi);
		renderer.WriteChildren(obj);
	}
}