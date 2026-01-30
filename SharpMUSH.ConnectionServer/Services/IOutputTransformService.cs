using SharpMUSH.ConnectionServer.Models;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Service for transforming output based on client capabilities and player preferences
/// </summary>
public interface IOutputTransformService
{
	/// <summary>
	/// Transforms raw output bytes based on client capabilities and player preferences
	/// </summary>
	/// <param name="rawOutput">The raw UTF-8 output bytes from the server</param>
	/// <param name="capabilities">The client's protocol capabilities</param>
	/// <param name="preferences">The player's output preferences (null if not logged in)</param>
	/// <returns>Transformed output bytes suitable for the client</returns>
	ValueTask<byte[]> TransformAsync(
		byte[] rawOutput,
		ProtocolCapabilities capabilities,
		PlayerOutputPreferences? preferences
	);
}
