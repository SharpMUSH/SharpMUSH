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
	/// <paramref name="rawOutput"/>, UTF-8, in the client's character set. UTF-8 output is the input itself,
	/// byte for byte; anything else is transcoded, and a character the set lacks becomes <c>?</c>.
	/// </summary>
	public static byte[] Transform(byte[] rawOutput, ProtocolCapabilities capabilities)
	{
		var targetEncoding = GetTargetEncoding(capabilities.Charset);
		return targetEncoding == Encoding.UTF8 ? rawOutput : targetEncoding.GetBytes(Encoding.UTF8.GetString(rawOutput));
	}

	private static Encoding GetTargetEncoding(string charset)
	{
		if (charset.Equals("ASCII", StringComparison.OrdinalIgnoreCase))
		{
			return Encoding.ASCII;
		}

		if (charset.Equals("LATIN-1", StringComparison.OrdinalIgnoreCase)
				|| charset.Equals("ISO-8859-1", StringComparison.OrdinalIgnoreCase))
		{
			return Encoding.Latin1;
		}

		// UTF-8, and the default for anything unrecognised.
		return Encoding.UTF8;
	}
}
