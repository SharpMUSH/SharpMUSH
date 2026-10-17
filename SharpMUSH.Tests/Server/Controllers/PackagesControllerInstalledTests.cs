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
		var source = Substitute.For<IPackageSourceService>();
		var manifests = Substitute.For<IPackageManifestService>();
		var directories = PluginPackageFixture.ScratchDirectories();
		var uploads = new PluginUploadStore(directories, manifests, NullLogger<PluginUploadStore>.Instance);
		var controller = new PackagesController(registry, source, manifests, Substitute.For<IPackageInstallService>(),
			Substitute.For<IPackageAuthoringService>(), Substitute.For<IPackageOperationRunner>(), uploads,
			new PackageManifestResolver(registry, source, manifests, uploads),
			Substitute.For<IAuditLog>(), new EngineVersion(new PackageVersion(2, 0, 0)));

		var result = (IReadOnlyList<InstalledPackageDto>)((OkObjectResult)(await controller.GetInstalled()).Result!).Value!;

		var byId = result.ToDictionary(p => p.Package.Id);
		await Assert.That(byId["core"].ManagedAttributeCount).IsEqualTo(7);
		await Assert.That(byId["core"].ObjectCount).IsEqualTo(2);
		await Assert.That(byId["core"].Dependents).IsEquivalentTo(new[] { "bbs", "jobs" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(byId["jobs"].Dependents).IsEquivalentTo(new[] { "bbs" });
		await Assert.That(byId["bbs"].Dependents).IsEmpty();
		await Assert.That(byId["bbs"].ManagedAttributeCount).IsEqualTo(3);
		PluginPackageFixture.Delete(directories);
		await registry.DidNotReceive().GetManagedAttributesAsync(Arg.Any<string>());
		await registry.DidNotReceive().GetPackageObjectsAsync(Arg.Any<string>());
		await registry.DidNotReceive().GetPackageDependentsAsync(Arg.Any<string>());
		await registry.Received(1).GetAllPackageDependenciesAsync();
	}
	[Test]
	public async Task RemoteTrustAndRemovalWaitForThePackageOperationGate()
	{
		var registry = Substitute.For<IPackageRegistryService>();
		var source = Substitute.For<IPackageSourceService>();
		var manifests = Substitute.For<IPackageManifestService>();
		var directories = PluginPackageFixture.ScratchDirectories();
		var uploads = new PluginUploadStore(directories, manifests, NullLogger<PluginUploadStore>.Instance);
		var controller = new PackagesController(registry, source, manifests, Substitute.For<IPackageInstallService>(),
			Substitute.For<IPackageAuthoringService>(), Substitute.For<IPackageOperationRunner>(), uploads,
			new PackageManifestResolver(registry, source, manifests, uploads), Substitute.For<IAuditLog>(), new(new(2, 0, 0)));
		var gate = new SharpMUSH.Library.Services.PackageOperationGate();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var held = gate.RunAsync(async () => { entered.TrySetResult(); await release.Task; return true; });
		try
		{
			await entered.Task;
			var update = controller.UpsertRemote(new("test", "https://example.test/repo", "official", null), gate);
			var remove = controller.DeleteRemote("other", gate);
			await Assert.That(update.IsCompleted).IsFalse();
			await Assert.That(remove.IsCompleted).IsFalse();
			release.TrySetResult();
			await Task.WhenAll(update, remove);
			await registry.Received(1).UpsertPackageRemoteAsync(Arg.Is<PackageRemoteRecord>(r => r.Name == "test" && r.Trust == PackageRemoteTrust.Official));
			await registry.Received(1).RemovePackageRemoteAsync("other");
		}
		finally { release.TrySetResult(); await held; PluginPackageFixture.Delete(directories); }
	}

}
