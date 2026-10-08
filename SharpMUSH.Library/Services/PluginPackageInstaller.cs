using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Default <see cref="IPluginPackageInstaller"/>: verifies a plugin package's carried binaries against the SHA-256
/// hashes in its manifest, checks its <c>plugin.json</c> against the package, and deposits the files into
/// <c>&lt;installed plugins&gt;/&lt;packageId&gt;/</c> so the loader picks them up at the next start. Uninstall removes
/// that directory and unloads the plugin if it is loaded and unloadable.
///
/// <para>Nothing is written until every check and every hash passes, and the files land in a staging folder that
/// replaces the old one only once complete — a rejected or interrupted apply leaves the plugins directory as it
/// was.</para>
/// </summary>
public sealed class PluginPackageInstaller(
	IPluginManager pluginManager,
	PluginInstallOptions options,
	PluginDirectories directories,
	ILogger<PluginPackageInstaller> logger) : IPluginPackageInstaller
{
	private static readonly JsonSerializerOptions ManifestJson = new() { PropertyNameCaseInsensitive = true };

	public async Task<Result<IReadOnlyList<string>>> DeployAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		IPluginPackageBinarySource binarySource,
		CancellationToken cancellationToken = default)
	{
		if (manifest.Binary is null)
		{
			return new Error<string>($"Plugin package '{manifest.Name}' has no 'binaries' block to deploy.");
		}

		if (!options.Allowed)
		{
			return new Error<string>(
				$"Plugin package '{manifest.Name}' was not installed: this server does not install plugin packages "
				+ $"({PluginDirectories.InstallVariable}=false).");
		}

		if (!request.AllowPluginCode)
		{
			return new Error<string>(
				$"Plugin package '{manifest.Name}' was not installed: a plugin runs compiled C# with full access to the "
				+ "server. Confirm that you trust its author (allow_plugin_code) to install it.");
		}

		if (!PluginContractVersion.Satisfies(manifest.Binary.MinServerVersion))
		{
			return new Error<string>(
				$"Plugin package '{manifest.Name}' requires plugin contract {manifest.Binary.MinServerVersion}; "
				+ $"this server provides {PluginContractVersion.Current}. Upgrade the server to install it.");
		}

		// Verify every file against its hash BEFORE writing anything.
		var verified = new List<(string FileName, byte[] Bytes)>();
		foreach (var file in manifest.Binary.Files)
		{
			var bytes = await binarySource.ReadBinaryAsync(file.FileName, cancellationToken);
			if (bytes is null)
			{
				return new Error<string>(
					$"Plugin package '{manifest.Name}': declared binary '{file.FileName}' is missing from the package source.");
			}

			var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
			if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return new Error<string>(
					$"Plugin package '{manifest.Name}': SHA-256 mismatch for '{file.FileName}' "
					+ $"(manifest {file.Sha256}, actual {actual}). Refusing to deploy tampered or stale binaries.");
			}

			verified.Add((file.FileName, bytes));
		}

		if (CheckPluginManifest(manifest.Name, verified) is { } refusal)
		{
			return new Error<string>($"Plugin package '{manifest.Name}': {refusal}");
		}

		var targetDirectory = Path.Combine(directories.Installed, manifest.Name);
		var staging = Path.Combine(directories.Installed, $".incoming-{manifest.Name}-{Guid.NewGuid():N}");
		try
		{
			Directory.CreateDirectory(staging);
			var deployed = new List<string>();
			foreach (var (fileName, bytes) in verified)
			{
				await File.WriteAllBytesAsync(Path.Combine(staging, fileName), bytes, cancellationToken);
				deployed.Add(fileName);
			}

			// Persist the verified SHA-256s next to the binaries so the runtime UI-assembly endpoint can
			// re-verify any compiled component it serves to the browser against the installer-checked hash.
			// Its absence simply means the endpoint cannot serve a component for this package (it 404s).
			var sidecar = PluginUiBinaryManifest.FromManifestFiles(manifest.Binary.Files);
			await File.WriteAllTextAsync(
				Path.Combine(staging, PluginUiBinaryManifest.FileName),
				sidecar.ToJson(),
				cancellationToken);

			if (Directory.Exists(targetDirectory))
			{
				Directory.Delete(targetDirectory, recursive: true);
			}

			Directory.Move(staging, targetDirectory);

			logger.LogInformation(
				"Deployed plugin package '{PackageId}' v{Version}: {Count} verified file(s) into {Directory}. "
				+ "It loads at the next restart.",
				manifest.Name, manifest.Version, deployed.Count, targetDirectory);

			return new Result<IReadOnlyList<string>>(deployed);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogError(ex, "Failed to deposit plugin package '{PackageId}' into {Directory}.",
				manifest.Name, targetDirectory);
			TryDelete(staging);
			return new Error<string>($"Plugin package '{manifest.Name}': failed to write binaries — {ex.Message}");
		}
	}

	/// <summary>
	/// Why the carried <c>plugin.json</c> cannot stand for package <paramref name="packageId"/>, or null when it can:
	/// it must be there, carry the package's id, not take the id of a plugin the server ships, and leave no doubt
	/// which DLL is the plugin.
	/// </summary>
	private string? CheckPluginManifest(string packageId, IReadOnlyList<(string FileName, byte[] Bytes)> files)
	{
		var manifestFile = files.FirstOrDefault(f => string.Equals(f.FileName, PluginManifest.FileName, StringComparison.OrdinalIgnoreCase));
		if (manifestFile.Bytes is null)
		{
			return $"it carries no {PluginManifest.FileName}; list one under 'binaries' with the plugin's id.";
		}

		PluginManifest? plugin;
		try
		{
			plugin = JsonSerializer.Deserialize<PluginManifest>(manifestFile.Bytes, ManifestJson);
		}
		catch (JsonException ex)
		{
			return $"its {PluginManifest.FileName} does not parse: {ex.Message}";
		}

		if (plugin is null || !string.Equals(plugin.Id, packageId, StringComparison.OrdinalIgnoreCase))
		{
			return $"its {PluginManifest.FileName} gives the id '{plugin?.Id}'; a plugin package's plugin id is its package id.";
		}

		if (BuiltInIds().Contains(packageId))
		{
			return $"a plugin that ships with this server already uses the id '{packageId}'.";
		}

		var dlls = files.Where(f => f.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
		return plugin.Entry is { Length: > 0 } entry
			? dlls.Any(d => string.Equals(d.FileName, entry, StringComparison.OrdinalIgnoreCase))
				? null
				: $"its {PluginManifest.FileName} names {entry} as the entry assembly, and the package does not carry it."
			: dlls.Count == 1
				? null
				: $"it carries {dlls.Count} DLLs; name the plugin's own in {PluginManifest.FileName} (\"entry\").";
	}

	/// <summary>The ids of the plugins the server ships, by folder or <c>plugin.json</c>.</summary>
	private HashSet<string> BuiltInIds()
	{
		var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(directories.BuiltIn))
		{
			return ids;
		}

		foreach (var folder in Directory.EnumerateDirectories(directories.BuiltIn))
		{
			ids.Add(Path.GetFileName(folder));
			var manifestPath = Path.Combine(folder, PluginManifest.FileName);
			if (!File.Exists(manifestPath))
			{
				continue;
			}

			try
			{
				if (JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), ManifestJson)?.Id is { Length: > 0 } id)
				{
					ids.Add(id);
				}
			}
			catch (JsonException)
			{
				// The loader reports a shipped plugin whose manifest does not parse; its folder name still counts.
			}
		}

		return ids;
	}

	private void TryDelete(string directory)
	{
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Could not remove the staging folder {Directory}.", directory);
		}
	}

	public async Task<Result<Success>> RemoveAsync(
		string packageId,
		IReadOnlyList<string> deployedFiles,
		CancellationToken cancellationToken = default)
	{
		// Unload first (if loaded + unloadable) so no assembly is pinned while we
		// delete its DLL; a load-once plugin cannot be unloaded at runtime, but its
		// directory is still removed so the next boot does not re-load it.
		switch (await pluginManager.UnloadAsync(packageId))
		{
			case Success:
				logger.LogInformation("Unloaded plugin package '{PackageId}' before removing its directory.", LogSanitizer.Sanitize(packageId));
				break;
			case Error<string> error:
				logger.LogDebug(
					"Plugin package '{PackageId}' not unloaded at runtime ({Reason}); removing its directory so it does not load on next boot.",
					LogSanitizer.Sanitize(packageId), error.Value);
				break;
		}

		var targetDirectory = Path.Combine(directories.Installed, packageId);
		try
		{
			if (Directory.Exists(targetDirectory))
			{
				Directory.Delete(targetDirectory, recursive: true);
				logger.LogInformation("Removed plugin package directory {Directory}.", LogSanitizer.Sanitize(targetDirectory));
			}

			return new Success();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogError(ex, "Failed to remove plugin package directory {Directory}.", LogSanitizer.Sanitize(targetDirectory));
			return new Error<string>($"Plugin package '{packageId}': failed to remove binaries — {ex.Message}");
		}
	}
}

/// <summary>
/// Whether this server installs plugin packages at all, from a remote or by upload. On unless the host sets
/// <c>SHARPMUSH_PLUGIN_INSTALL=false</c>: a plugin package is installed by someone who runs the server and chose
/// it, and the per-apply <see cref="PackageApplyRequest.AllowPluginCode"/> confirmation still applies.
/// </summary>
/// <param name="Allowed">Whether plugin packages may be installed.</param>
public sealed record PluginInstallOptions(bool Allowed)
{
	/// <summary>Reads <see cref="PluginDirectories.InstallVariable"/>: anything but <c>false</c> leaves it on.</summary>
	public static PluginInstallOptions FromEnvironment() =>
		new(!string.Equals(Environment.GetEnvironmentVariable(PluginDirectories.InstallVariable), "false",
			StringComparison.OrdinalIgnoreCase));
}
