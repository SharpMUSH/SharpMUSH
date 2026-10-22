using Mediator;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Packages;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Server;

public class PluginUninstallSafetyTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OrdinaryUninstallRefusesPluginDependenciesEvenWhenForced(bool force)
	{
		var registry = Substitute.For<IPackageRegistryService>();
		registry.GetInstalledPackageAsync("owner").Returns(new InstalledPackageRecord("owner", "1", "repo", null, "sha", null,
			DateTimeOffset.UnixEpoch, 1, DeployedFiles: ["plugin.dll"], Kind: PackageKind.Plugin));
		var admin = Substitute.For<IPluginAdministration>();
		admin.GetBlockersAsync("owner", false, Arg.Any<CancellationToken>()).Returns(new PluginStateBlockers(["content"], ["dependent"]));
		var files = Substitute.For<IPluginPackageInstaller>();
		files.RemoveAsync("owner", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new Success());
		var (objects, attributes, flags, applications, mediator, locks) = (Substitute.For<IObjectStore>(), Substitute.For<IAttributeStore>(),
			Substitute.For<IFlagAndPowerStore>(), Substitute.For<IApplicationRegistryService>(), Substitute.For<IMediator>(), Substitute.For<ILockService>());
		var service = new PackageInstallService(objects, attributes,
			flags, Substitute.For<INavigationStore>(), registry, applications,
			Substitute.For<IPackagePlanService>(), new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create()),
			Substitute.For<IPackageLifecycleRunner>(), files, mediator, new PackageOperationGate(),
			locks, Substitute.For<IPackageDeclarationService>(), Substitute.For<IPackageSettingService>(),
			Substitute.For<IPackageWikiService>(), new Lazy<IRuntimeRegistrationService>(() => Substitute.For<IRuntimeRegistrationService>()),
			new EngineVersion(new PackageVersion(2, 0, 0)), new Lazy<IPluginManager>(() => Substitute.For<IPluginManager>()), new Lazy<IPluginAdministration>(() => admin),
			TestPackageWrites.Factory(objects, attributes, flags, registry, applications, locks, mediator));

		await Assert.That(await service.UninstallAsync("owner", force) is Error<string>).IsTrue();
		await registry.DidNotReceive().RemoveInstalledPackageAsync("owner");
		await files.DidNotReceive().RemoveAsync("owner", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
	}
}
