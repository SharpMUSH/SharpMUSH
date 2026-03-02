using Markdig.Syntax.Inlines;
using System.Drawing;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiCodeInlineRenderer : AsciiObjectRenderer<CodeInline>
{
	// Light-blue (#9CDCFE) applied to inline code spans, matching the RecursiveMarkdownRenderer palette.
	private static readonly Ansi _inlineCodeStyle = Ansi.Create(
		foreground: ANSILibrary.StringExtensions.rgb(Color.FromArgb(0x9C, 0xDC, 0xFE)));

	protected override void Write(MarkdownToAsciiRenderer renderer, CodeInline obj)
	{
		renderer.Write(MModule.markupSingle(_inlineCodeStyle, obj.Content));
	}
}