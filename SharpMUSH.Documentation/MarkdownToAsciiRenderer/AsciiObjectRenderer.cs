using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// A base class for HTML rendering <see cref="Block"/> and <see cref="Inline"/> Markdown objects.
/// </summary>
/// <typeparam name="TObject">The type of the object.</typeparam>
/// <seealso cref="IMarkdownObjectRenderer" />
public abstract class AsciiObjectRenderer<TObject> : MarkdownObjectRenderer<MarkdownToAsciiRenderer, TObject>
	where TObject : MarkdownObject;