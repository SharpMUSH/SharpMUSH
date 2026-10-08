using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// <c>GET /api/packages</c> counts what each package manages without reading the baselines, and reads the
/// dependency table once for every package instead of scanning it per package.
/// </summary>
public class PackagesControllerInstalledTests
{
	private static InstalledPackageRecord Package(string id) =>
		new(id, "1.0.0", "repo", $"{id}/", "commit", "main", DateTimeOffset.UnixEpoch, 1);

	[Test]
	public async Task CountsWithoutBaselinesAndGroupsDependentsFromOneRead()
	{
		var registry = Substitute.For<IPackageRegistryService>();
		registry.GetInstalledPackagesAsync().Returns([Package("core"), Package("jobs"), Package("bbs")]);
		registry.CountManagedAttributesAsync("core").Returns(7);
		registry.CountPackageObjectsAsync("core").Returns(2);
		registry.CountManagedAttributesAsync("bbs").Returns(3);
		registry.GetAllPackageDependenciesAsync().Returns([
			new PackageDependencyRecord("jobs", "core", ""),
			new PackageDependencyRecord("bbs", "core", ">=1.0"),
			new PackageDependencyRecord("bbs", "jobs", "")
		]);
		var controller = new PackagesController(registry, Substitute.For<IPackageSourceService>(),
			Substitute.For<IPackageManifestService>(), Substitute.For<IPackageInstallService>(),
			Substitute.For<IPackageAuthoringService>(), Substitute.For<IPackageOperationRunner>(),
			new PluginUploadStore(PluginPackageFixture.ScratchDirectories(), Substitute.For<IPackageManifestService>(),
				NullLogger<PluginUploadStore>.Instance),
			Substitute.For<IAuditLog>());

		var result = (IReadOnlyList<InstalledPackageDto>)((OkObjectResult)(await controller.GetInstalled()).Result!).Value!;

		var byId = result.ToDictionary(p => p.Package.Id);
		await Assert.That(byId["core"].ManagedAttributeCount).IsEqualTo(7);
		await Assert.That(byId["core"].ObjectCount).IsEqualTo(2);
		await Assert.That(byId["core"].Dependents).IsEquivalentTo(new[] { "bbs", "jobs" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(byId["jobs"].Dependents).IsEquivalentTo(new[] { "bbs" });
		await Assert.That(byId["bbs"].Dependents).IsEmpty();
		await Assert.That(byId["bbs"].ManagedAttributeCount).IsEqualTo(3);
		await registry.DidNotReceive().GetManagedAttributesAsync(Arg.Any<string>());
		await registry.DidNotReceive().GetPackageObjectsAsync(Arg.Any<string>());
		await registry.DidNotReceive().GetPackageDependentsAsync(Arg.Any<string>());
		await registry.Received(1).GetAllPackageDependenciesAsync();
	}
}
