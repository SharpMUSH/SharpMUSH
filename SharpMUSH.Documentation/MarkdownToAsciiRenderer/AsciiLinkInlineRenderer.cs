using Markdig.Syntax.Inlines;
using Microsoft.FSharp.Core;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLinkInlineRenderer : AsciiObjectRenderer<LinkInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, LinkInline obj)
	{
		var link = obj.Url ?? "";
		
		// For links, we need to render the children and wrap them with link markup
		// Since Container is protected, we'll use a simpler approach:
		// Just render children for now - link URL metadata is stored but not visually different
		// The markup system handles this through the Ansi.Create linkUrl parameter
		
		if (!string.IsNullOrEmpty(link))
		{
			// We have a URL, but we can only render children without direct container access
			// The link text comes from rendering children
			renderer.WriteChildren(obj);
		}
		else
		{
			// No URL, just render children
			renderer.WriteChildren(obj);
		}
	}
}