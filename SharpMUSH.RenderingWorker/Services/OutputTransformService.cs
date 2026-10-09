using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;
using System.Text;

namespace SharpMUSH.RenderingWorker.Services;

/// <summary>
/// Writes output in the character set the client asked for. Colour is not this service's concern: the
/// renderer already wrote each colour at the depth the connection can display
/// (<see cref="MarkupOutputRenderer"/>).
/// </summary>
public sealed class OutputTransformService : IOutputTransformService
{
	public ValueTask<byte[]> TransformAsync(byte[] rawOutput, ProtocolCapabilities capabilities, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(Transform(rawOutput, capabilities));
	}

	/// <summary>
	/// <paramref name="rawOutput"/>, UTF-8, in the connection's character set
	/// (<see cref="ProtocolCapabilities.OutputCharset"/>). UTF-8 output is the input itself, byte for byte; anything
	/// else has each character the set lacks replaced with the nearest one it has
	/// (<see cref="MarkupOutputRenderer.FoldFor"/>), then is transcoded. Rendered output was folded already, before
	/// it was laid out; this catches output sent as it is.
	/// </summary>
	/// <param name="translations">The game's <c>ascii_translations</c> table, or null for the built-in stand-ins alone.</param>
	public static byte[] Transform(byte[] rawOutput, ProtocolCapabilities capabilities,
		IReadOnlyDictionary<string, string>? translations = null)
	{
		if (MarkupOutputRenderer.FoldFor(capabilities, translations) is not { } fold) return rawOutput;
		var text = fold.Fold(Encoding.UTF8.GetString(rawOutput));
		return (fold.Latin1 ? Encoding.Latin1 : Encoding.ASCII).GetBytes(text);
	}
}
