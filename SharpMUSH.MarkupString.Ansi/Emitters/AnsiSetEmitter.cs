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

		var effective = AnsiEmitterSupport.Fold(set);
		var previous = AnsiEmitterSupport.Fold(context.Previous);

		using var core = new PooledCharWriter(body.Length + 32);
		SgrWriter.Transition(previous, effective, core);
		AnsiEmitterSupport.WriteHyperlinked(effective, body, core);

		// Nothing follows that would diff this state away, so close it here rather than leaving the
		// terminal coloured for whatever the connection writes next.
		if (context.Next is null && LeavesState(effective)) SgrWriter.Reset(core);

		AnsiEmitterSupport.WriteWrapped(set, core.WrittenSpan, context, output);
		return true;
	}

	/// <summary>
	/// Whether the style leaves the terminal in a state that has to be closed. A link alone does
	/// not — OSC 8 closes itself — and neither does <see cref="AnsiStyle.Clear"/>, which is the
	/// reset.
	/// </summary>
	private static bool LeavesState(in AnsiStyle style) =>
		style.Foreground is not null || style.Background is not null
		|| style.Bold || style.Faint || style.Italic || style.Underlined
		|| style.Overlined || style.Blink || style.Inverted || style.StrikeThrough;
}
