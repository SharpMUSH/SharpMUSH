namespace MarkupString.Ansi;

/// <summary>
/// A markup layer that carries terminal formatting, whether or not it is an
/// <see cref="AnsiMarkup"/>. The emitters in this package fold every layer of a run that offers a
/// style into one <see cref="AnsiStyle"/>, so a bold layer nested inside a red one becomes a single
/// SGR sequence (or a single HTML span) rather than two nested wrappers.
/// </summary>
/// <remarks>
/// A layer that returns <see langword="false"/> is left alone: the set emitter delegates it to its
/// own <see cref="IMarkupEmitter"/> for the format, wrapping the folded output. That is how a tag
/// with no terminal equivalent — <c>&lt;send&gt;</c>, say — keeps its own rendering.
/// </remarks>
public interface IAnsiStyleSource
{
	/// <summary>The style this layer contributes, if it has one.</summary>
	bool TryGetAnsiStyle(out AnsiStyle style);
}
