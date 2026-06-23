using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Default <see cref="IPluginUiAssemblyProvider"/>: reads a managed plugin's UI assembly from
/// <c>plugins/{pluginId}/{assembly}</c> and verifies its bytes against the install-time SHA-256 recorded in
/// the package's <c>plugin-ui.json</c> sidecar before returning them. Any failure — unknown plugin, traversal
/// attempt, missing sidecar, unlisted assembly, or hash mismatch — resolves to <see cref="NotFound"/>.
/// </summary>
public sealed class FileSystemPluginUiAssemblyProvider(
	ILogger<FileSystemPluginUiAssemblyProvider> logger,
	string? pluginsRoot = null) : IPluginUiAssemblyProvider
{
	private readonly string _pluginsRoot = pluginsRoot
		?? Path.Combine(AppContext.BaseDirectory, "plugins");

	public async Task<OneOf<byte[], NotFound>> GetVerifiedAssemblyAsync(
		string pluginId, string assembly, CancellationToken cancellationToken = default)
	{
		// Reject any path-bearing identifiers up front: both segments must be flat file/dir names. This blocks
		// "../" traversal and absolute paths before we touch the filesystem.
		if (!IsFlatName(pluginId) || !IsFlatName(assembly))
		{
			logger.LogWarning("Rejected plugin UI assembly request with non-flat id/assembly: '{Plugin}'/'{Assembly}'.",
				pluginId, assembly);
			return new NotFound();
		}

		var pluginDirectory = Path.Combine(_pluginsRoot, pluginId);
		var sidecarPath = Path.Combine(pluginDirectory, PluginUiBinaryManifest.FileName);
		if (!File.Exists(sidecarPath))
		{
			// No install-time hash sidecar → no trust anchor → refuse to serve.
			return new NotFound();
		}

		PluginUiBinaryManifest? sidecar;
		try
		{
			sidecar = PluginUiBinaryManifest.FromJson(await File.ReadAllTextAsync(sidecarPath, cancellationToken));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Failed to read plugin UI sidecar for '{Plugin}'.", pluginId);
			return new NotFound();
		}

		var expected = sidecar?.HashFor(assembly);
		if (string.IsNullOrEmpty(expected))
		{
			// The assembly is not a declared (and hashed) binary of this package.
			return new NotFound();
		}

		var assemblyPath = Path.Combine(pluginDirectory, assembly);
		if (!File.Exists(assemblyPath))
		{
			return new NotFound();
		}

		byte[] bytes;
		try
		{
			bytes = await File.ReadAllBytesAsync(assemblyPath, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Failed to read plugin UI assembly '{Assembly}' for '{Plugin}'.", assembly, pluginId);
			return new NotFound();
		}

		var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
		{
			logger.LogWarning(
				"Plugin UI assembly '{Assembly}' for '{Plugin}' failed hash verification (expected {Expected}, actual {Actual}); refusing to serve.",
				assembly, pluginId, expected, actual);
			return new NotFound();
		}

		return bytes;
	}

	/// <summary>True only for a single flat path segment (no separators, no traversal, non-empty).</summary>
	private static bool IsFlatName(string value) =>
		!string.IsNullOrWhiteSpace(value)
		&& value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0
		&& value != "."
		&& value != ".."
		&& !Path.IsPathRooted(value);
}
