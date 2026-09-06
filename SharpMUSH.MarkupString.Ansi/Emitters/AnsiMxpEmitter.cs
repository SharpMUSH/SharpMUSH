using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run for an MXP client: colours and attributes as SGR, command links as
/// <c>&lt;SEND&gt;</c> and URL links as <c>&lt;A HREF&gt;</c>.
/// </summary>
public sealed class AnsiMxpEmitter : IMarkupSetEmitter
{
	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Mxp;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);
		AnsiEmitterSupport.EmitTagged(set, body, context, output, TagFlavour.Mxp);
		return true;
	}
}
