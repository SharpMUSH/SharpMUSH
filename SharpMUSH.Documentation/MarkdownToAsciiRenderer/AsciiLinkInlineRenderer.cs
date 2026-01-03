using Markdig.Syntax.Inlines;
using Microsoft.FSharp.Core;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLinkInlineRenderer : AsciiObjectRenderer<LinkInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, LinkInline obj)
	{
		var link = obj.Url ?? "";
		
		// Create link markup
		var linkOption = new FSharpOption<string>(link);
		// For now, just render the children without special link formatting
		// TODO: Properly wrap link children in link markup when we have a way to capture inline content
		renderer.WriteChildren(obj);
	}
}