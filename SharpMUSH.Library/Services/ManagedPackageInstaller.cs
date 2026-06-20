using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Default <see cref="IManagedPackageInstaller"/>: verifies a managed package's
/// carried binaries against the SHA-256 hashes in its manifest, enforces the
/// two-part trust gate, and deposits the verified files into
/// <c>plugins/&lt;packageId&gt;/</c> so the plugin loader picks them up on the next
/// boot. Uninstall removes that directory and unloads the plugin if it is loaded
/// and unloadable.
///
/// <para>Nothing is written until every gate and every hash passes — a rejected
/// apply leaves the plugins directory untouched.</para>
/// </summary>
public sealed class ManagedPackageInstaller(
	IPluginManager pluginManager,
	ManagedPackageTrustOptions trustOptions,
	ILogger<ManagedPackageInstaller> logger,
	string? pluginsRoot = null) : IManagedPackageInstaller
{
	private readonly string _pluginsRoot = pluginsRoot
		?? Path.Combine(AppContext.BaseDirectory, "plugins");

	public async Task<OneOf<IReadOnlyList<string>, Error<string>>> DeployAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		IManagedPackageBinarySource binarySource,
		CancellationToken cancellationToken = default)
	{
		if (manifest.Binary is null)
		{
			return new Error<string>($"Managed package '{manifest.Name}' has no 'binaries' block to deploy.");
		}

		// ── Trust gate (two parts, both required) ──────────────────────────────
		if (!request.AllowManagedCode)
		{
			return new Error<string>(
				$"Managed package '{manifest.Name}' was not installed: installing a managed package deposits and runs "
				+ "arbitrary compiled C# in full server trust. Re-run the install with the explicit managed-code "
				+ "opt-in (allow_managed_code) to confirm you trust this package's author.");
		}

		if (!trustOptions.IsAllowed(manifest.Name))
		{
			return new Error<string>(
				$"Managed package '{manifest.Name}' is not on the server's managed-package allow-list. "
				+ "Add its id to ManagedPackages:AllowList (or enable AllowAll) before installing compiled code.");
		}

		// ── Server/contract compatibility ──────────────────────────────────────
		if (!PluginContractVersion.Satisfies(manifest.Binary.MinServerVersion))
		{
			return new Error<string>(
				$"Managed package '{manifest.Name}' requires server/plugin-contract version {manifest.Binary.MinServerVersion}; "
				+ $"this server provides {PluginContractVersion.Current}. Upgrade the server to install it.");
		}

		// ── Verify every file against its hash BEFORE writing anything ─────────
		var verified = new List<(string FileName, byte[] Bytes)>();
		foreach (var file in manifest.Binary.Files)
		{
			var bytes = await binarySource.ReadBinaryAsync(file.FileName, cancellationToken);
			if (bytes is null)
			{
				return new Error<string>(
					$"Managed package '{manifest.Name}': declared binary '{file.FileName}' is missing from the package source.");
			}

			var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
			if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return new Error<string>(
					$"Managed package '{manifest.Name}': SHA-256 mismatch for '{file.FileName}' "
					+ $"(manifest {file.Sha256}, actual {actual}). Refusing to deploy tampered or stale binaries.");
			}

			verified.Add((file.FileName, bytes));
		}

		// ── Deposit (clean re-deploy: replace any prior directory for this id) ──
		var targetDirectory = Path.Combine(_pluginsRoot, manifest.Name);
		try
		{
			if (Directory.Exists(targetDirectory))
			{
				Directory.Delete(targetDirectory, recursive: true);
			}

			Directory.CreateDirectory(targetDirectory);
			var deployed = new List<string>();
			foreach (var (fileName, bytes) in verified)
			{
				var path = Path.Combine(targetDirectory, fileName);
				await File.WriteAllBytesAsync(path, bytes, cancellationToken);
				deployed.Add(fileName);
			}

			logger.LogInformation(
				"Deployed managed package '{PackageId}' v{Version}: {Count} verified file(s) into {Directory}. "
				+ "It loads on the next server boot.",
				manifest.Name, manifest.Version, deployed.Count, targetDirectory);

			return OneOf<IReadOnlyList<string>, Error<string>>.FromT0(deployed);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogError(ex, "Failed to deposit managed package '{PackageId}' into {Directory}.",
				manifest.Name, targetDirectory);
			return new Error<string>($"Managed package '{manifest.Name}': failed to write binaries — {ex.Message}");
		}
	}

	public async Task<OneOf<Success, Error<string>>> RemoveAsync(
		string packageId,
		IReadOnlyList<string> deployedFiles,
		CancellationToken cancellationToken = default)
	{
		// Unload first (if loaded + unloadable) so no assembly is pinned while we
		// delete its DLL; a load-once plugin cannot be unloaded at runtime, but its
		// directory is still removed so the next boot does not re-load it.
		var unload = await pluginManager.UnloadAsync(packageId);
		unload.Switch(
			_ => logger.LogInformation("Unloaded managed package '{PackageId}' before removing its directory.", packageId),
			error => logger.LogDebug(
				"Managed package '{PackageId}' not unloaded at runtime ({Reason}); removing its directory so it does not load on next boot.",
				packageId, error.Value));

		var targetDirectory = Path.Combine(_pluginsRoot, packageId);
		try
		{
			if (Directory.Exists(targetDirectory))
			{
				Directory.Delete(targetDirectory, recursive: true);
				logger.LogInformation("Removed managed package directory {Directory}.", targetDirectory);
			}

			return new Success();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogError(ex, "Failed to remove managed package directory {Directory}.", targetDirectory);
			return new Error<string>($"Managed package '{packageId}': failed to remove binaries — {ex.Message}");
		}
	}
}

/// <summary>
/// Server-side trust configuration for managed (compiled-DLL) packages. The
/// allow-list is the operator's standing pre-approval of which package ids may
/// deposit code; it is the second half of the Phase-4 trust gate (the first is
/// the per-apply <see cref="PackageApplyRequest.AllowManagedCode"/> opt-in).
/// Never self-declared by a package — always server config.
/// </summary>
/// <param name="AllowAll">When true, any package id is permitted (single-operator / dev convenience). Default false.</param>
/// <param name="AllowList">Explicitly permitted package ids (case-insensitive).</param>
public sealed record ManagedPackageTrustOptions(bool AllowAll, IReadOnlyCollection<string> AllowList)
{
	private readonly HashSet<string> _allowed =
		new(AllowList ?? [], StringComparer.OrdinalIgnoreCase);

	/// <summary>The default: no managed packages permitted (must be configured explicitly).</summary>
	public static ManagedPackageTrustOptions Denied { get; } = new(false, []);

	public bool IsAllowed(string packageId) => AllowAll || _allowed.Contains(packageId);
}
