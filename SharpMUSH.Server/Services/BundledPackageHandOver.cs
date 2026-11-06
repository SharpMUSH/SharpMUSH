using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Re-points the install of a package that left the bundled catalogue at the official remote library. The record
/// keeps its version, revisions and baselines; only where it came from changes, so the package manager checks the
/// library for its updates and applies them over the copy the game has, as for any package installed from a remote.
/// </summary>
public static class BundledPackageHandOver
{
	/// <summary>
	/// Hands over each installed package that <paramref name="moved"/> names, came from the catalogue, and this build no
	/// longer ships. The library is the configured remote at <see cref="OfficialPackageRemote.Url"/>; without one, that
	/// remote is added, because browsing, reviewing and applying an upgrade all read a configured remote. Returns the
	/// ids handed over.
	/// </summary>
	public static async Task<IReadOnlyList<string>> RunAsync(IPackageRegistryService registry,
		IReadOnlyDictionary<string, string> moved, ILogger logger)
	{
		if (moved.Count == 0)
		{
			return [];
		}

		var candidates = (await registry.GetInstalledPackagesAsync())
			.Where(package => BundledPackages.IsCatalogueSource(package.SourceRepo)
				&& moved.ContainsKey(package.Id)
				&& !BundledPackages.Contains(package.Id))
			.ToList();
		if (candidates.Count == 0)
		{
			return [];
		}

		var library = await LibraryAsync(registry, logger);

		foreach (var package in candidates)
		{
			var path = moved[package.Id].Trim('/');
			await registry.UpsertInstalledPackageAsync(package with { SourceRepo = library, SourcePath = path, PinnedBranch = null });
			logger.LogInformation("Bundled {PackageId} v{Version} no longer ships with this server; its install now follows {Library} ({Path}).",
				package.Id, package.Version, library, path);
		}

		return candidates.Select(package => package.Id).ToList();
	}

	private static async Task<string> LibraryAsync(IPackageRegistryService registry, ILogger logger)
	{
		var remotes = await registry.GetPackageRemotesAsync();

		// The stored URL, as written, so the update check and the browse page match the install to this remote.
		if (remotes.FirstOrDefault(r => OfficialPackageRemote.IsLibrary(r.Url)) is { } configured)
		{
			return configured.Url;
		}

		// A remote of the administrator's own under the library's name is left as it is.
		if (remotes.Any(r => string.Equals(r.Name, OfficialPackageRemote.Name, StringComparison.OrdinalIgnoreCase)))
		{
			logger.LogWarning("The remote {Name} is not the official library; add {Library} as a remote to browse the moved packages' updates.",
				OfficialPackageRemote.Name, OfficialPackageRemote.Url);
			return OfficialPackageRemote.Url;
		}

		await registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			OfficialPackageRemote.Name, OfficialPackageRemote.Url, PackageRemoteTrust.Official, null));
		logger.LogInformation("Added the official package library {Library} as a remote for the packages that moved there.",
			OfficialPackageRemote.Url);
		return OfficialPackageRemote.Url;
	}
}
