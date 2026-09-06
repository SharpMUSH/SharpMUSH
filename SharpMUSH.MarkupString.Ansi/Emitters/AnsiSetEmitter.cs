using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run to a terminal. Every layer of the run that carries a style folds into one
/// <see cref="AnsiStyle"/>, which is diffed against the style the previous run left in effect, so
/// the stream carries only what actually changes. The state is closed with <c>ESC[0m</c> when the
/// run is the last one or plain text follows it; between adjacent runs the next run's diff does it.
/// </summary>
public sealed class AnsiSetEmitter : IMarkupSetEmitter
{
	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Ansi;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);

		var effective = AnsiEmitterSupport.Fold(set, context.Format);
		var previous = AnsiEmitterSupport.Fold(context.Previous, context.Format);

		using var core = new PooledCharWriter(body.Length + 32);
		SgrWriter.Transition(previous, effective, core);
		AnsiEmitterSupport.WriteHyperlinked(effective, body, core);

		// Nothing follows that would diff this state away, so close it here rather than leaving the
		// terminal coloured for whatever the connection writes next.
		if (context.Next is null && AnsiEmitterSupport.LeavesState(effective)) SgrWriter.Reset(core);

		AnsiEmitterSupport.WriteWrapped(set, core.WrittenSpan, context, output);
		return true;
	}
}
