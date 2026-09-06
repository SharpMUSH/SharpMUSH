using System.Buffers;
namespace MarkupString;

/// <summary>
/// Renders one markup layer in one format. Registered per <c>(MarkupType, Format)</c> pair; a run
/// carrying a layer with no emitter for the format has that layer pass its body through unchanged.
/// </summary>
public interface IMarkupEmitter
{
	/// <summary>The <see cref="IMarkup"/> implementation this emits.</summary>
	Type MarkupType { get; }

	/// <summary>The format this emits for.</summary>
	MarkupFormat Format { get; }

	/// <summary>
	/// Writes the opening sequence, <paramref name="body"/> and the closing sequence to
	/// <paramref name="output"/>. <paramref name="body"/> is the already-encoded run text with
	/// every inner layer applied.
	/// </summary>
	void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output);
}
