using Markdig.Renderers;
using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class MarkdownToAsciiRenderer : TextRendererBase<MarkdownToAsciiRenderer>
{
	public MarkdownToAsciiRenderer(TextWriter writer) : base(writer)
	{
		ObjectRenderers.Add(new AsciiCodeBlockRenderer());
		ObjectRenderers.Add(new AsciiListRenderer());
		ObjectRenderers.Add(new AsciiHeadingRenderer());
		ObjectRenderers.Add(new AsciiHtmlBlockRenderer());
		ObjectRenderers.Add(new AsciiParagraphRenderer());
		ObjectRenderers.Add(new AsciiQuoteBlockRenderer());
		ObjectRenderers.Add(new AsciiThematicBreakRenderer());

		// Default inline renderers
		ObjectRenderers.Add(new AsciiAutolinkInlineRenderer());
		ObjectRenderers.Add(new AsciiCodeInlineRenderer());
		ObjectRenderers.Add(new AsciiDelimiterInlineRenderer());
		ObjectRenderers.Add(new AsciiEmphasisInlineRenderer());
		ObjectRenderers.Add(new AsciiLineBreakInlineRenderer());
		ObjectRenderers.Add(new AsciiHtmlInlineRenderer());
		ObjectRenderers.Add(new AsciiHtmlEntityInlineRenderer());
		ObjectRenderers.Add(new AsciiLinkInlineRenderer());
		ObjectRenderers.Add(new AsciiLiteralInlineRenderer());
	}

	/// <summary>
	/// Writes the lines of a <see cref="LeafBlock"/>
	/// </summary>
	/// <param name="leafBlock">The leaf block.</param>
	/// <param name="writeEndOfLines">if set to <c>true</c> write end of lines.</param>
	/// <param name="escape">if set to <c>true</c> escape the content for HTML</param>
	/// <param name="softEscape">Only escape &lt; and &amp;</param>
	/// <returns>This instance</returns>
	public MarkdownToAsciiRenderer WriteLeafRawLines(LeafBlock leafBlock, bool writeEndOfLines, bool escape,
		bool softEscape = false)
	{
		ArgumentNullException.ThrowIfNull(leafBlock);

		var slices = leafBlock.Lines.Lines;
		
		for (var i = 0; i < slices.Length; i++)
		{
			ref var slice = ref slices[i].Slice;
			if (slice.Text is null)
			{
				break;
			}

			if (!writeEndOfLines && i > 0)
			{
				WriteLine();
			}

			var span = slice.AsSpan();
			if (escape)
			{
				// WriteEscape(span, softEscape);
				Write(span);
			}
			else
			{
				Write(span);
			}

			if (writeEndOfLines)
			{
				WriteLine();
			}
		}

		return this;
	}
}

public class AsciiCodeBlockRenderer : AsciiObjectRenderer<CodeBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, CodeBlock obj)
	{
		throw new NotImplementedException();
	}
}