using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Whether this world has been given the official package library as a remote (expanded server data). It is set
/// once, so a library the administrator removed stays removed.
/// </summary>
public sealed class OfficialPackageRemoteOffered
{
	public bool Offered { get; set; }
}

/// <summary>
/// Gives a world the official package library, <see cref="OfficialPackageRemote.Url"/>, as a configured remote, so an
/// administrator can browse and install its packages without adding it first.
/// </summary>
public static class OfficialPackageRemoteDefault
{
	/// <summary>
	/// Adds the library as an official remote the first time a world boots on a build that has this, unless a remote
	/// already points at the library or holds its name. Returns true when it was added.
	/// </summary>
	public static async Task<bool> RunAsync(IPackageRegistryService registry, IExpandedObjectDataService serverData,
		ILogger logger)
	{
		if ((await serverData.GetExpandedServerDataAsync<OfficialPackageRemoteOffered>())?.Offered ?? false)
		{
			return false;
		}

		var remotes = await registry.GetPackageRemotesAsync();
		var add = !remotes.Any(r => OfficialPackageRemote.IsLibrary(r.Url)
			|| string.Equals(r.Name, OfficialPackageRemote.Name, StringComparison.OrdinalIgnoreCase));
		if (add)
		{
			await registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
				OfficialPackageRemote.Name, OfficialPackageRemote.Url, PackageRemoteTrust.Official, null));
			logger.LogInformation("Added the official package library {Library} as the remote {Name}.",
				OfficialPackageRemote.Url, OfficialPackageRemote.Name);
		}

		await serverData.SetExpandedServerDataAsync(new OfficialPackageRemoteOffered { Offered = true });
		return add;
	}
}
