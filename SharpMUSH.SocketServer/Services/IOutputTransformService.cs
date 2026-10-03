using SharpMUSH.SocketServer.Models;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// Writes output in the character set a client asked for. Colour is already at the client's depth by then:
/// the markup renderer wrote it that way.
/// </summary>
public interface IOutputTransformService
{
	/// <summary>
	/// <paramref name="rawOutput"/> in the client's character set.
	/// </summary>
	/// <param name="rawOutput">The UTF-8 output bytes</param>
	/// <param name="capabilities">The client's protocol capabilities</param>
	/// <param name="ct">Cancels the work, including waiting for a remote worker.</param>
	ValueTask<byte[]> TransformAsync(
		byte[] rawOutput,
		ProtocolCapabilities capabilities,
		CancellationToken ct = default
	);
}
