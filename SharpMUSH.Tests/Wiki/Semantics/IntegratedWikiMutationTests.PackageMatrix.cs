using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task EarlierPausedDirectPackageEntrySurvivesCancelledWaiterAndPermanentRefusal(int entry)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var gate = host.Services.GetRequiredService<IPackageOperationGate>();
		var started = NewSignal();
		var release = NewSignal();
		var held = gate.RunAsync(async () => { started.TrySetResult(); await release.Task; return 0; });
		await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		Task? parent = null;
		Task<Result<PackageApplyResult>>? apply = null;
		try
		{
			parent = entry switch
			{
				0 => installer.UninstallAsync("fixture-matrix-missing"),
				1 => installer.RollbackAsync("fixture-matrix-missing", 1),
				_ => apply = installer.ApplyAsync(MatrixManifest(), MatrixRequest())
			};
			await Assert.That(parent.IsCompleted).IsFalse();
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
			using var cancellation = new CancellationTokenSource();
			var waiter = parent.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.That(() => waiter).Throws<OperationCanceledException>();
			await Assert.That(parent.IsCompleted).IsFalse();
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		}
		finally
		{
			release.TrySetResult();
			await held;
			if (parent is not null) await parent;
		}
		if (apply is not null) (await apply).Expect<PackageApplyResult>();
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	[Test]
	public async Task EarlierQueuedRunnerPreventsActivationBeforeBackupProvenanceAndBody()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var scheduler = host.Services.GetRequiredService<ITaskScheduler>();
		var started = NewSignal();
		var release = NewSignal();
		var finished = NewSignal();
		var hold = await scheduler.AdmitSocketWork(async () => { started.TrySetResult(); await release.Task; return null; },
			"fixture-before-package", "fixture", () => finished.TrySetResult());
		await Assert.That(hold.Accepted).IsTrue();
		await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var files = BackupFiles(host);
		var body = false;
		var provenance = false;
		Task<PackageOperationOutcome<int>>? parent = null;
		try
		{
			parent = host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("fixture-queued",
				_ => { body = true; return Task.FromResult(7); }, provenance: _ =>
				{ provenance = true; return Task.FromResult(new BackupProvenance("pre-package", Operation: "fixture-queued")); });
			await Assert.That(parent.IsCompleted).IsFalse();
			await Assert.That(body || provenance).IsFalse();
			await Assert.That(BackupFiles(host)).IsEquivalentTo(files);
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
			using var cancellation = new CancellationTokenSource();
			var waiter = parent.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.That(() => waiter).Throws<OperationCanceledException>();
			await Assert.That(parent.IsCompleted).IsFalse();
		}
		finally
		{
			release.TrySetResult();
			await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
			if (parent is not null) await parent;
		}
		await Assert.That((await parent!).Expect<PackageOperationRan<int>>().Result).IsEqualTo(7);
		await Assert.That(body).IsTrue();
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
	}

	[Test]
	public async Task EarlierRunnerDeferredHookRetainsPermanentAdmissionAcrossCancelledWaiter()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var scheduler = host.Services.GetRequiredService<ITaskScheduler>();
		var lifecycle = host.Services.GetRequiredService<IPackageLifecycleRunner>();
		var blocked = NewSignal();
		var release = NewSignal();
		var finished = NewSignal();
		var parent = host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("fixture-deferred", async token =>
		{
			// The actual production lifecycle runner defers this valid hook during the actual runner's body.
			await lifecycle.RunLifecycleAsync("#1", "AINSTALL", token);
			var queued = await scheduler.AdmitSocketWork(async () => { blocked.TrySetResult(); await release.Task; return null; },
				"fixture-before-deferred-hook", "fixture", () => finished.TrySetResult());
			if (!queued.Accepted) throw new InvalidOperationException("Owned queue barrier was refused.");
			return 9;
		});
		try
		{
			await blocked.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(parent.IsCompleted).IsFalse();
			using var cancellation = new CancellationTokenSource();
			var waiter = parent.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.That(() => waiter).Throws<OperationCanceledException>();
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		}
		finally
		{
			release.TrySetResult();
			await parent;
			await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
		}
		await Assert.That((await parent).Expect<PackageOperationRan<int>>().Result).IsEqualTo(9);
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
	}

	[Test]
	public async Task EarlierFailedRunnerAndNestedDirectEntryPermanentlyPreventActivation()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var runner = host.Services.GetRequiredService<IPackageOperationRunner>();
		await Assert.That(() => runner.RunAsync<int>("fixture-nested-failure", async _ =>
		{
			await installer.UninstallAsync("fixture-matrix-missing");
			throw new ActivationFault();
		})).Throws<ActivationFault>();
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
	}

	[Test]
	public async Task ActiveEntriesRefuseBeforeHeldQueueGateBackupAndProvenance()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var gateStarted = NewSignal();
		var release = NewSignal();
		var held = host.Services.GetRequiredService<IPackageOperationGate>().RunAsync(async () =>
		{ gateStarted.TrySetResult(); await release.Task; return 0; });
		await gateStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var queueStarted = NewSignal();
		var queueFinished = NewSignal();
		var queued = await host.Services.GetRequiredService<ITaskScheduler>().AdmitSocketWork(async () =>
		{ queueStarted.TrySetResult(); await release.Task; return null; }, "fixture-active-package-barrier", "fixture", () => queueFinished.TrySetResult());
		await Assert.That(queued.Accepted).IsTrue();
		await queueStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var pending = new List<Task>();
		try
		{
			var files = BackupFiles(host);
			var invoked = false;
			var runner = host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("fixture-active-held",
				_ => { invoked = true; return Task.FromResult(1); }, provenance: _ => { invoked = true; throw new ActivationFault(); });
			pending.Add(runner);
			await Assert.That(runner.IsCompleted).IsTrue();
			await Assert.That(await runner is PackageOperationRefused).IsTrue();
			var installer = host.Services.GetRequiredService<IPackageInstallService>();
			var apply = installer.ApplyAsync(MatrixManifest(), MatrixRequest());
			var uninstall = installer.UninstallAsync("fixture-matrix-missing");
			var rollback = installer.RollbackAsync("fixture-matrix-missing", 1);
			pending.AddRange([apply, uninstall, rollback]);
			await Assert.That(apply.IsCompleted && uninstall.IsCompleted && rollback.IsCompleted).IsTrue();
			await Assert.That(await apply is Error<string>).IsTrue();
			await Assert.That(await uninstall is Error<string>).IsTrue();
			await Assert.That(await rollback is Error<string>).IsTrue();
			await Assert.That(invoked).IsFalse();
			await Assert.That(BackupFiles(host)).IsEquivalentTo(files);
		}
		finally
		{
			release.TrySetResult();
			await held;
			await queueFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Task.WhenAll(pending);
		}
	}

	[Test]
	public async Task ReadOnlyPackagePlanAndReadmeDoNotPrecludeActivation()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		await installer.PlanAsync(MatrixManifest());
		await installer.GetReadmeAsync("fixture-matrix-missing");
		await host.ActivateAtomicWikiProjectionAsync();
	}

	[Test]
	public async Task CancelledActualRunnerWaiterCannotAdmitActivationWhileItsBodyStillRuns()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		using var cancellation = new CancellationTokenSource();
		var started = NewSignal();
		var release = NewSignal();
		var bodyFinished = NewSignal();
		var parent = host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("fixture-cancelled-running", async _ =>
		{
			started.TrySetResult();
			await release.Task;
			bodyFinished.TrySetResult();
			return 1;
		}, cancellation.Token);
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			cancellation.Cancel();
			await Assert.That(() => parent).Throws<OperationCanceledException>();
			await Assert.That(bodyFinished.Task.IsCompleted).IsFalse();
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		}
		finally
		{
			release.TrySetResult();
			await bodyFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await host.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests(TimeSpan.FromSeconds(30));
			try { await parent; } catch (OperationCanceledException) { }
		}
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
	}

	[Test]
	public async Task CancelledActualDirectEntryKeepsPermanentAdmissionAfterGateWaitEnds()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var release = NewSignal();
		var started = NewSignal();
		var held = host.Services.GetRequiredService<IPackageOperationGate>().RunAsync(async () =>
		{ started.TrySetResult(); await release.Task; return 0; });
		await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
		using var cancellation = new CancellationTokenSource();
		var parent = host.Services.GetRequiredService<IPackageInstallService>().UninstallAsync("fixture-cancelled-direct", cancellationToken: cancellation.Token);
		try
		{
			cancellation.Cancel();
			await Assert.That(() => parent).Throws<OperationCanceledException>();
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		}
		finally { release.TrySetResult(); await held; try { await parent; } catch (OperationCanceledException) { } }
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
	}

	private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
	private static string[] BackupFiles(SemanticWikiPrototypeHost host)
		=> Directory.GetFiles(host.WorldPath + ".backups", "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
	private static PackageManifest MatrixManifest() => new(new(1, 7), "fixture-matrix", new(1, 0, 0), ["fixture"], "fixture", null,
		null, [], null, null, null, [], [], new Dictionary<string, PackageConfigureSpec>(), []);
	private static PackageApplyRequest MatrixRequest() => new(new("https://example.invalid/fixture", null, "fixture-commit", null),
		new Dictionary<string, string>(), []);
}
