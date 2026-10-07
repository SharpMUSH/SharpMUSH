using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Package operations run one at a time (#1484). The gate itself, and that apply, uninstall and
/// rollback each wait for it. The install service under test has a gate of its own, so holding it
/// here never holds up another test's package operations.
/// </summary>
public class PackageOperationGateTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	/// <summary>Long enough that an operation not waiting for the gate would have finished.</summary>
	private static readonly TimeSpan Held = TimeSpan.FromMilliseconds(500);

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;

	private static PackageApplySource Source(string id) => new(
		"https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", "commit-1", "main");

	private PackageInstallService InstallerWith(IPackageOperationGate gate)
	{
		var services = WebAppFactoryArg.Services;
		return new PackageInstallService(
			Database, Database, Database, Database, Registry, (IApplicationRegistryService)Database,
			services.GetRequiredService<IPackagePlanService>(),
			services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			services.GetRequiredService<IPackageLifecycleRunner>(),
			services.GetRequiredService<IManagedPackageInstaller>(),
			services.GetRequiredService<IMediator>(),
			gate,
			services.GetRequiredService<ILockService>(),
			services.GetRequiredService<IPackageDeclarationService>(),
			services.GetRequiredService<IPackageSettingService>(),
			services.GetRequiredService<Lazy<IRuntimeRegistrationService>>());
	}

	private static PackageManifest Manifest(string id, string version) =>
		new PackageManifestService().ParseManifest($"""
			package: {id}
			version: "{version}"
			objects:
			  - ref: marker
			    type: thing
			    name: Gate Marker
			    attributes:
			      FN_VERSION: "{version}"
			""") switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};

	private static PackageApplyRequest Request(string id) => new(Source(id), new Dictionary<string, string>(), []);

	/// <summary>Holds <paramref name="gate"/> until the returned source is completed; returns once it holds it.</summary>
	private static async Task<(TaskCompletionSource Release, Task Holder)> HoldAsync(IPackageOperationGate gate)
	{
		var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var holder = gate.RunAsync(async () =>
		{
			holding.TrySetResult();
			await release.Task;
			return 0;
		});
		await holding.Task.WaitAsync(TimeSpan.FromSeconds(10));
		return (release, holder);
	}

	[Test]
	public async Task ASecondOperation_WaitsForTheFirstToFinish()
	{
		var gate = new PackageOperationGate();
		var (release, holder) = await HoldAsync(gate);

		var second = gate.RunAsync(() => Task.FromResult(true));
		await Task.Delay(Held);
		await Assert.That(second.IsCompleted).IsFalse();

		release.SetResult();
		await holder;
		await Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(10))).IsTrue();
	}

	[Test]
	public async Task AnOperationInsideTheGate_RunsANestedOneWithoutWaitingForItself()
	{
		var gate = new PackageOperationGate();

		var nested = await gate.RunAsync(() => gate.RunAsync(() => Task.FromResult("nested")))
			.WaitAsync(TimeSpan.FromSeconds(10));

		await Assert.That(nested).IsEqualTo("nested");
	}

	[Test]
	public async Task AnOperationThatThrows_ReleasesTheGate()
	{
		var gate = new PackageOperationGate();

		await Assert.That(async () => await gate.RunAsync<int>(() => throw new InvalidOperationException("boom")))
			.Throws<InvalidOperationException>();

		await Assert.That(await gate.RunAsync(() => Task.FromResult(1)).WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo(1);
	}

	[Test]
	public async Task TheHoldDoesNotLeakToTheCaller()
	{
		var gate = new PackageOperationGate();
		await gate.RunAsync(() => Task.FromResult(0));

		// Had the caller's flow kept the hold, this would run straight through instead of waiting.
		var (release, holder) = await HoldAsync(gate);
		var other = Task.Run(() => gate.RunAsync(() => Task.FromResult(0)));
		await Task.Delay(Held);
		await Assert.That(other.IsCompleted).IsFalse();
		release.SetResult();
		await holder;
		await other.WaitAsync(TimeSpan.FromSeconds(10));
	}

	[Test]
	public async Task Apply_WaitsForTheGateBeforeReadingOrWriting()
	{
		var gate = new PackageOperationGate();
		var installer = InstallerWith(gate);
		var id = $"gate-apply-{Guid.NewGuid():N}";
		var (release, holder) = await HoldAsync(gate);

		var apply = installer.ApplyAsync(Manifest(id, "1.0"), Request(id));
		await Task.Delay(Held);
		await Assert.That(apply.IsCompleted).IsFalse();
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Value).IsTypeOf<NotFound>()
			.Because("an apply waiting for the gate has written nothing");

		release.SetResult();
		await holder;
		(await apply.WaitAsync(TimeSpan.FromSeconds(30))).Expect<PackageApplyResult>();
		(await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		(await installer.UninstallAsync(id)).Expect<Success>();
	}

	[Test]
	public async Task RollbackAndUninstall_WaitForTheGate()
	{
		var gate = new PackageOperationGate();
		var installer = InstallerWith(gate);
		var id = $"gate-rollback-{Guid.NewGuid():N}";
		(await installer.ApplyAsync(Manifest(id, "1.0"), Request(id))).Expect<PackageApplyResult>();
		(await installer.ApplyAsync(Manifest(id, "1.1"), Request(id))).Expect<PackageApplyResult>();

		var (release, holder) = await HoldAsync(gate);
		var rollback = installer.RollbackAsync(id, 1);
		await Task.Delay(Held);
		await Assert.That(rollback.IsCompleted).IsFalse();
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>().Version)
			.IsEqualTo("1.1.0").Because("a rollback waiting for the gate has written nothing");
		release.SetResult();
		await holder;
		(await rollback.WaitAsync(TimeSpan.FromSeconds(30))).Expect<PackageRollbackResult>();

		(release, holder) = await HoldAsync(gate);
		var uninstall = installer.UninstallAsync(id);
		await Task.Delay(Held);
		await Assert.That(uninstall.IsCompleted).IsFalse();
		(await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		release.SetResult();
		await holder;
		(await uninstall.WaitAsync(TimeSpan.FromSeconds(30))).Expect<Success>();
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Value).IsTypeOf<NotFound>();
	}
}
