using OneOf;
using OneOf.Types;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Serves a managed plugin's compiled UI assembly bytes to the WASM client, re-verifying them against the
/// Phase-4 install-time SHA-256 sidecar before returning. Unknown plugin, unknown/unlisted assembly, or a
/// hash mismatch all resolve to <see cref="NotFound"/> — never tampered or stale bytes.
/// </summary>
public interface IPluginUiAssemblyProvider
{
	/// <summary>
	/// Read and verify the bytes for <paramref name="assembly"/> under plugin <paramref name="pluginId"/>.
	/// Returns the verified bytes, or <see cref="NotFound"/> when the plugin/assembly is unknown, the sidecar
	/// is missing, or the on-disk bytes do not match the recorded hash.
	/// </summary>
	Task<OneOf<byte[], NotFound>> GetVerifiedAssemblyAsync(
		string pluginId, string assembly, CancellationToken cancellationToken = default);
}
