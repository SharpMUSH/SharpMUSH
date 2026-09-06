using System.Text.Json;
namespace MarkupString;

/// <summary>
/// Serialises one markup layer to and from the JSON envelope. The <c>"k"</c> kind discriminator is
/// written by the serializer, not by the codec; a codec writes and reads its own properties only.
/// </summary>
public interface IMarkupCodec
{
	/// <summary>The wire discriminator for this markup, e.g. <c>"ansi"</c>.</summary>
	string Kind { get; }

	/// <summary>The <see cref="IMarkup"/> implementation this serialises.</summary>
	Type MarkupType { get; }

	/// <summary>Writes the markup's properties into the object the serializer has already started.</summary>
	void Write(Utf8JsonWriter writer, IMarkup markup);

	/// <summary>Reads a markup back from the object the serializer read.</summary>
	IMarkup Read(JsonElement element);
}
