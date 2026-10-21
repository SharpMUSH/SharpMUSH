using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Applications;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// The fixture stays retained for as long as package work it admitted can still run: queued lifecycle hooks
/// after the body returned, and a managed package's deployment, which opens no package transaction.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	/// <summary>Holds every lifecycle hook entry at a gate, running the real runner's deferral and hooks around it.</summary>
	private sealed class GatedLifecycle(PackageLifecycleRunner inner, TaskCompletionSource started, TaskCompletionSource release)
		: IPackageLifecycleRunner, IPackageLifecycleObserver
	{
		public Task RunLifecycleAsync(PackageChangeset changeset, IReadOnlyDictionary<string, string> createdObjects, CancellationToken cancellationToken = default)
			=> inner.RunLifecycleAsync(changeset, createdObjects, cancellationToken);
		public Task RunLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default)
			=> inner.RunLifecycleAsync(objId, attribute, cancellationToken);
		public async Task<PackageLifecycleOutcome> RunObservedLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default)
		{
			started.TrySetResult();
			await release.Task;
			return await inner.RunObservedLifecycleAsync(objId, attribute, cancellationToken);
		}
	}

	private sealed class GatedFiles(TaskCompletionSource started, TaskCompletionSource release) : IPackageFileSource
	{
		public async Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
		{
			started.TrySetResult();
			await release.Task;
			return null;
		}
	}

	private SemanticWikiPrototypeHost GatedHookHost(TaskCompletionSource started, TaskCompletionSource release) => NewHost(services =>
	{
		services.RemoveAll<IPackageLifecycleRunner>();
		services.AddSingleton<PackageLifecycleRunner>();
		services.AddSingleton<IPackageLifecycleRunner>(sp => new GatedLifecycle(sp.GetRequiredService<PackageLifecycleRunner>(), started, release));
	});

	private static PackageManifest HookedManifest() => new PackageManifestService().ParseManifest("""
		package: retention-hooks
		version: "1.0"
		objects:
		  - ref: marker
		    type: thing
		    name: Retention Marker
		""") switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CloseWaitsForQueuedHooksAfterTheBodyReturned(bool run)
	{
		var started = NewSignal();
		var release = NewSignal();
		await using var host = GatedHookHost(started, release);
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var runner = host.Services.GetRequiredService<IPackageOperationRunner>();
		Func<CancellationToken, Task<Result<PackageApplyResult>>> body = _ => installer.ApplyAsync(HookedManifest(), MatrixRequest());
		Task pending = run
			? runner.RunAsync("retention-hooks", body)
			: (await ((IPackageOperationLifetime)runner).StartAsync("retention-hooks", body)).Completion;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(pending.IsCompleted).IsFalse();
			// Bounded: without a hold, close proceeds and then waits on the gated entry, which would deadlock the test.
			await Assert.That(async () => await host.CloseAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Throws<SemanticFixtureOperationInProgressException>();
		}
		finally { release.TrySetResult(); }
		await pending;
		await host.CloseAsync();
	}

	[Test]
	public async Task CloseWaitsForAManagedPackageApplyThatOpensNoTransaction()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var started = NewSignal();
		var release = NewSignal();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var manifest = UiPackageDeploymentTests.Manifest(new() { ["index.html"] = "<html></html>"u8.ToArray() });
		var request = new PackageApplyRequest(new("repo", manifest.Name, "commit", "main"), new Dictionary<string, string>(), [], 10, AllowPluginCode: true);
		var apply = installer.ApplyAsync(manifest, request, CancellationToken.None, new GatedFiles(started, release));
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			// Bounded: without a hold, close proceeds and then waits on the gated entry, which would deadlock the test.
			await Assert.That(async () => await host.CloseAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Throws<SemanticFixtureOperationInProgressException>();
		}
		finally { release.TrySetResult(); }
		await Assert.That(await apply is Error<string>).IsTrue();
		await host.CloseAsync();
	}
}
