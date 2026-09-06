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
	/// <summary>
	/// The style this layer contributes to <paramref name="format"/>, if it has one there.
	/// </summary>
	/// <remarks>
	/// The answer is per-format so a layer can fold where a terminal style is all the format can
	/// express and keep its own rendering where the format has a better one: a bold tag can return
	/// <see langword="true"/> for <see cref="MarkupFormat.Ansi"/> and <see langword="false"/> for
	/// <see cref="MarkupFormat.Html"/>, folding into the SGR run in the one and being delegated to
	/// its own <see cref="IMarkupEmitter"/> — <c>&lt;b&gt;</c> — in the other.
	/// </remarks>
	/// <param name="format">The format being rendered.</param>
	/// <param name="style">The contributed style; meaningless when the result is <see langword="false"/>.</param>
	bool TryGetAnsiStyle(MarkupFormat format, out AnsiStyle style);
}
