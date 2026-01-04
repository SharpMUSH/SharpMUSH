using Markdig.Syntax.Inlines;
using Microsoft.FSharp.Core;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLinkInlineRenderer : AsciiObjectRenderer<LinkInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, LinkInline obj)
	{
		// For links, we render the children which provides the visible link text
		// The URL is stored in obj.Url but without container access we can't wrap it in link markup
		// The markup system can handle link metadata through Ansi.Create linkUrl parameter in future enhancements
		renderer.WriteChildren(obj);
	}
}