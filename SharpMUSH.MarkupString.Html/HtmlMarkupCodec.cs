using System.Text.Json;
namespace MarkupString.Html;

/// <summary>
/// Reads and writes <see cref="HtmlMarkup"/> in the serializer's envelope, under kind
/// <c>"html"</c>. Keys are <c>h</c> (tag name) and <c>a</c> (attributes, written only when
/// non-empty). Legacy rows written before the <c>"k"</c> discriminator existed carry <c>h</c> with
/// no <c>k</c> at all; <see cref="MarkupTextSerializer"/>'s own inference routes those here without
/// this codec having to know about it.
/// </summary>
public sealed class HtmlMarkupCodec : IMarkupCodec
{
	/// <inheritdoc/>
	public string Kind => "html";

	/// <inheritdoc/>
	public Type MarkupType => typeof(HtmlMarkup);

	/// <inheritdoc/>
	public void Write(Utf8JsonWriter writer, IMarkup markup)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(markup);

		var html = (HtmlMarkup)markup;
		writer.WriteString("h", html.TagName);
		if (html.Attributes is { Length: > 0 } attributes) writer.WriteString("a", attributes);
	}

	/// <inheritdoc/>
	public IMarkup Read(JsonElement element)
	{
		var tagName = element.TryGetProperty("h", out var tag) && tag.ValueKind == JsonValueKind.String
			? tag.GetString() ?? string.Empty
			: string.Empty;

		var attributes = element.TryGetProperty("a", out var attrs) && attrs.ValueKind == JsonValueKind.String
			? attrs.GetString()
			: null;

		return new HtmlMarkup(tagName, attributes);
	}
}
