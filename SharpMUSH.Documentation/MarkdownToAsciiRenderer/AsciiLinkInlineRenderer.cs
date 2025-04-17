using Markdig.Syntax.Inlines;
using Microsoft.FSharp.Core;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLinkInlineRenderer : AsciiObjectRenderer<LinkInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, LinkInline obj)
	{
		var link = obj.Url;
		var label = obj.Label ?? obj.Url;
		var linkOption = new FSharpOption<string>(link ?? "");
		var labelOption = new FSharpOption<string>(label ?? "");
		var ascii = MModule.markupSingle(Ansi.Create(linkUrl: linkOption, linkText: labelOption), label);
		
		renderer.Write(ascii.ToString());
	}
}