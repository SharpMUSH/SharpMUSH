using System.Buffers;
namespace MarkupString;

/// <summary>
/// Renders a whole <see cref="MarkupSet"/> at once, so layers that must fold into a single
/// sequence (several SGR attributes into one escape) can do so. Registered per format and
/// consulted before the per-layer emitters for every run; layers it does not own are its own to
/// delegate, through <see cref="EmitContext.Registry"/>.
/// </summary>
public interface IMarkupSetEmitter
{
	/// <summary>The format this emits for.</summary>
	MarkupFormat Format { get; }

	/// <summary>
	/// Writes the whole run to <paramref name="output"/> and returns <see langword="true"/>, or
	/// returns <see langword="false"/> to leave the run to the per-layer emitters. Nothing written
	/// to <paramref name="output"/> reaches the render when it returns <see langword="false"/>.
	/// </summary>
	bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output);
}
