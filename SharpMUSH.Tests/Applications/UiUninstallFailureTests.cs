using System.Text.Json;
using Mediator;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Applications;

public class UiUninstallFailureTests
{
	[Test]
	public async Task RejectedForcedUninstallPreservesBothInstanceConfigs()
	{
		var registry = Substitute.For<IPackageRegistryService>();
		registry.GetInstalledPackageAsync("graph-proof").Returns(new InstalledPackageRecord("graph-proof", "1.0", "repo", "path", "commit", "main", DateTimeOffset.UtcNow, 1, ["ui.json", "index.html"], Kind: PackageKind.Ui));
		registry.GetPackageDependentsAsync("graph-proof").Returns(Array.Empty<PackageDependencyRecord>());
		var apps = new FakeApplicationRegistry();
		foreach (var (slug, room) in new[] { ("east", 10), ("west", 20) })
			await apps.UpsertApplicationAsync(new(slug, slug, null, ApplicationKind.Page, "", null, null, PortalRole.Player, null, null, 0,
				RenderKind: ApplicationRenderKind.Hosted, DefinitionId: "graph-proof", Config: JsonSerializer.SerializeToElement(new { rooms = new[] { room } })));
		var files = Substitute.For<IPluginPackageInstaller>();
		files.RemoveAsync("graph-proof", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new Error<string>("Removal refused: ambiguous directories."));
		var service = new PackageInstallService(Substitute.For<IObjectStore>(), Substitute.For<IAttributeStore>(), Substitute.For<IFlagAndPowerStore>(), Substitute.For<INavigationStore>(),
			registry, apps, Substitute.For<IPackagePlanService>(), new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create()), Substitute.For<IPackageLifecycleRunner>(), files,
			Substitute.For<IMediator>(), new PackageOperationGate(), Substitute.For<ILockService>(), Substitute.For<IPackageDeclarationService>(), Substitute.For<IPackageSettingService>(), Substitute.For<IPackageWikiService>(),
			new Lazy<IRuntimeRegistrationService>(() => Substitute.For<IRuntimeRegistrationService>()), new EngineVersion(new PackageVersion(2, 0, 0)));
		await Assert.That(await service.UninstallAsync("graph-proof", force: true) is Error<string>).IsTrue();
		await Assert.That((await apps.GetApplicationsAsync()).Count).IsEqualTo(2);
		await Assert.That((await apps.GetApplicationAsync("east")).Expect<RegisteredApplication>().Config!.Value.GetProperty("rooms")[0].GetInt32()).IsEqualTo(10);
		await Assert.That((await apps.GetApplicationAsync("west")).Expect<RegisteredApplication>().Config!.Value.GetProperty("rooms")[0].GetInt32()).IsEqualTo(20);
	}
}
