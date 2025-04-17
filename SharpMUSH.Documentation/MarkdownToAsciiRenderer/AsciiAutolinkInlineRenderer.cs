using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiAutolinkInlineRenderer : AsciiObjectRenderer<AutolinkInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, AutolinkInline obj)
	{
		throw new NotImplementedException();
	}
}