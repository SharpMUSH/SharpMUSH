using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// The coordinator's root reaches the hook entries its operation admits, so hooks that ran no code let
/// a package with objects settle; hooks that ran code, or hook entries that outlive the operation, do not.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static PackageManifest ObjectManifest(string package, string? hook) => new PackageManifestService().ParseManifest($"""
		package: {package}
		version: "1.0"
		objects:
		  - ref: marker
		    type: thing
		    name: Hook Marker
		    attributes:
		      FN_VERSION: "1.0"
		{(hook is null ? "" : $"      AINSTALL: \"{hook}\"")}
		""") switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	[Test]
	[Arguments(null, true)]
	[Arguments("", true)]
	[Arguments("think installed", false)]
	public async Task HooksJoinTheCoordinatorAndOnlyHooksThatRanNoCodeSettle(string? hook, bool settles)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var execution = await ((IPackageOperationLifetime)host.Services.GetRequiredService<IPackageOperationRunner>())
			.StartAsync("hooks", _ => installer.ApplyAsync(ObjectManifest("hook-settlement", hook), MatrixRequest()));
		var outcome = await execution.Completion;
		var observation = await execution.Observation;
		await Assert.That(outcome.Value is PackageOperationRan<Result<PackageApplyResult>>).IsTrue();
		await Assert.That(observation.HooksCollected).IsGreaterThan(0);
		await Assert.That(observation.HooksReleased).IsEqualTo(observation.HooksCollected);
		await Assert.That(Settled(host)).IsEqualTo(settles);
		await host.CloseAsync();
	}

	[Test]
	public async Task AHookEntryThatRunsAfterReleaseTakesItsOwnAttempt()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var services = host.Services;
		var installer = services.GetRequiredService<IPackageInstallService>();
		var scope = services.GetRequiredService<IPackageOperationScope>();
		object? captured = null;
		await Coordinated(host, async _ =>
		{
			captured = scope.Capture();
			return await installer.ApplyAsync(MatrixManifest(), MatrixRequest());
		});
		await Assert.That(captured is SemanticFixturePackageOperation { IsReleased: true }).IsTrue();
		await Assert.That(Settled(host)).IsTrue();
		var settledGeneration = PackageGeneration(host);

		using (scope.Enter(captured))
			await ((IPackageLifecycleObserver)services.GetRequiredService<IPackageLifecycleRunner>())
				.RunObservedLifecycleAsync("#7", "AINSTALL");

		await Assert.That(Settled(host)).IsFalse();
		await Assert.That(PackageGeneration(host)).IsEqualTo(settledGeneration + 1);
		await host.CloseAsync();
	}
}
