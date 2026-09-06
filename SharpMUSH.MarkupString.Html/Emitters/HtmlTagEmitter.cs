using System.Buffers;
namespace MarkupString.Html;

/// <summary>
/// Renders an <see cref="HtmlMarkup"/> layer as its own tag: <c>&lt;{TagName} {Attributes}&gt;
/// body&lt;/{TagName}&gt;</c>, or <c>&lt;{TagName}&gt;body&lt;/{TagName}&gt;</c> when there are no
/// attributes. One instance is registered per format — <see cref="MarkupFormat.Html"/>,
/// <see cref="MarkupFormat.Pueblo"/> and <see cref="MarkupFormat.Mxp"/> — because these formats all
/// want the tag written verbatim, unlike Ansi/BBCode, where <see cref="HtmlMarkup"/> folds a
/// handful of tags into terminal styling instead (see <see cref="HtmlMarkup.TryGetAnsiStyle"/>).
/// </summary>
/// <remarks>Neither the tag name, the attributes, nor the body are encoded — the tag is written raw.</remarks>
public sealed class HtmlTagEmitter(MarkupFormat format) : IMarkupEmitter
{
	/// <inheritdoc/>
	public Type MarkupType => typeof(HtmlMarkup);

	/// <inheritdoc/>
	public MarkupFormat Format { get; } = format;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		var html = (HtmlMarkup)markup;

		output.Write("<");
		output.Write(html.TagName);
		if (html.Attributes is { Length: > 0 } attributes)
		{
			output.Write(" ");
			output.Write(attributes);
		}
		output.Write(">");
		output.Write(body);
		output.Write("</");
		output.Write(html.TagName);
		output.Write(">");
	}
}
