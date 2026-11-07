using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A package that leaves the bundled catalogue keeps its installs: bootstrap re-points each one at the official
/// remote library instead of leaving it as a package nothing ships.
/// </summary>
public class BundledPackageHandOverTests
{
	private static readonly DateTimeOffset InstalledAt = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

	private static InstalledPackageRecord Bundled(string id) => new(
		id, "1.4.0", BundledPackageSource.SourceRepo, id, BundledPackageSource.SourceCommit, null, InstalledAt, 3);

	private static IPackageRegistryService Registry(IReadOnlyList<InstalledPackageRecord> installed,
		params PackageRemoteRecord[] remotes)
	{
		var registry = Substitute.For<IPackageRegistryService>();
		registry.GetInstalledPackagesAsync().Returns(installed);
		registry.GetPackageRemotesAsync().Returns(remotes);
		return registry;
	}

	private static IReadOnlyDictionary<string, string> Moved(params (string Id, string Path)[] moves)
		=> moves.ToDictionary(m => m.Id, m => m.Path, StringComparer.OrdinalIgnoreCase);

	[Test]
	public async Task MovedPackage_FollowsTheOfficialLibrary_WhenNoneIsConfigured()
	{
		var registry = Registry([Bundled("left-pkg")]);

		var handedOver = await BundledPackageHandOver.RunAsync(registry, Moved(("left-pkg", "left-pkg/")),
			NullLogger.Instance);

		await Assert.That(handedOver).IsEquivalentTo(["left-pkg"]);
		await registry.Received(1).UpsertInstalledPackageAsync(Bundled("left-pkg") with
		{
			SourceRepo = OfficialPackageRemote.Url,
			SourcePath = "left-pkg"
		});
		// Browse, review and apply read configured remotes, so the library is added for the upgrade to be reachable.
		await registry.Received(1).UpsertPackageRemoteAsync(new PackageRemoteRecord(
			OfficialPackageRemote.Name, OfficialPackageRemote.Url, PackageRemoteTrust.Official, null));
	}

	/// <summary>An administrator's remote under the library's name is not overwritten.</summary>
	[Test]
	public async Task RemoteUnderTheLibrarysName_IsLeftAlone()
	{
		var registry = Registry([Bundled("left-pkg")],
			new PackageRemoteRecord(OfficialPackageRemote.Name, "https://git.example.org/mine", PackageRemoteTrust.Community, null));

		await BundledPackageHandOver.RunAsync(registry, Moved(("left-pkg", "left-pkg")), NullLogger.Instance);

		await registry.DidNotReceive().UpsertPackageRemoteAsync(Arg.Any<PackageRemoteRecord>());
		await registry.Received(1).UpsertInstalledPackageAsync(Arg.Is<InstalledPackageRecord>(p => p.SourceRepo == OfficialPackageRemote.Url));
	}

	/// <summary>
	/// The library as the game configured it wins, under its stored spelling, so the update check and the browse page
	/// match the install to it; another official remote of the game's own does not have the moved package.
	/// </summary>
	[Test]
	[Arguments("https://github.com/SharpMUSH/SharpMUSH.Packages.git")]
	[Arguments("https://github.com/sharpmush/sharpmush.packages/")]
	[Arguments("https://github.com/SharpMUSH/SharpMUSH-Packages")]
	public async Task MovedPackage_FollowsTheConfiguredLibrary(string stored)
	{
		var registry = Registry([Bundled("left-pkg")],
			new PackageRemoteRecord("Ours", "https://git.example.org/ours", PackageRemoteTrust.Official, null),
			new PackageRemoteRecord("Library", stored, PackageRemoteTrust.Community, "main"));

		await BundledPackageHandOver.RunAsync(registry, Moved(("left-pkg", "games/left-pkg")), NullLogger.Instance);

		await registry.Received(1).UpsertInstalledPackageAsync(Bundled("left-pkg") with
		{
			SourceRepo = stored,
			SourcePath = "games/left-pkg"
		});
		await registry.DidNotReceive().UpsertPackageRemoteAsync(Arg.Any<PackageRemoteRecord>());
	}

	/// <summary>
	/// The same id installed from a remote is the administrator's own; one this build still ships, or one dropped
	/// without a new home, is not moved.
	/// </summary>
	[Test]
	public async Task OnlyCatalogueInstallsOfMovedPackagesAreHandedOver()
	{
		var fromRemote = Bundled("forked-pkg") with { SourceRepo = "https://git.example.org/fork", InstalledCommit = new string('a', 40) };
		var stillShipped = Bundled("plus-help");
		var dropped = Bundled("dropped-pkg");
		var registry = Registry([fromRemote, stillShipped, dropped]);

		var handedOver = await BundledPackageHandOver.RunAsync(registry,
			Moved(("forked-pkg", "forked-pkg"), ("plus-help", "plus-help")), NullLogger.Instance);

		await Assert.That(handedOver).IsEmpty();
		await registry.DidNotReceive().UpsertInstalledPackageAsync(Arg.Any<InstalledPackageRecord>());
	}

	/// <summary>
	/// A bridge that stays while its package moves would never install again: bootstrap does not put a bundled bridge
	/// over a package installed from a remote. A package moves with its bridges.
	/// </summary>
	[Test]
	public async Task MovedPackages_HaveLeftTheCatalogue_WithTheirBridges()
	{
		var manifests = new PackageManifestService();
		foreach (var (id, path) in BundledPackages.Moved)
		{
			await Assert.That(BundledPackages.Contains(id)).IsFalse().Because($"{id} is listed as moved but still ships");
			await Assert.That(path.Trim('/')).IsNotEmpty();
			var bridges = BundledPackages.All
				.Where(p => p.Bridge && BundledPackages.Dependencies(p.PackageId, manifests).Contains(id, StringComparer.OrdinalIgnoreCase))
				.Select(p => p.PackageId)
				.ToList();
			await Assert.That(bridges).IsEmpty().Because($"{id} moved but its bridges {string.Join(", ", bridges)} still ship");
		}
	}
}
