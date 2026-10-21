using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// The fixture's package coordinator. It takes one retained attempt per operation, owns the operation's
/// flow with it, and settles that attempt's generation only after joining the operation's completion
/// and observation, and only when everything it can see certifies success.
/// </summary>
internal sealed class SemanticWikiPackageOperationRunner : IPackageOperationRunner, IPackageOperationLifetime
{
	private readonly PackageOperationRunner _inner;
	private readonly LightningDatabase _provider;
	private readonly HashSet<Task> _settling = [];
	internal SemanticFixtureCapability Owner { get; }
	internal SemanticWikiPackageOperationRunner(PackageOperationRunner inner, SemanticFixtureCapability owner, LightningDatabase provider)
	{
		_inner = inner;
		_provider = provider;
		Owner = owner;
		owner.RegisterPackageFence(runner: true);
	}

	public async Task<PackageOperationOutcome<T>> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
	{
		if (!Owner.AdmitPackageMutation())
			return new PackageOperationRefused("Package mutations are unavailable in this atomic wiki fixture slice.");
		var execution = await StartOwnedAsync(operation, body, cancellationToken, provenance);
		return await execution.Completion.WaitAsync(cancellationToken);
	}

	public async Task<PackageOperationExecution<T>> StartAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
	{
		if (!Owner.AdmitPackageMutation()) throw SemanticFixtureCapability.Unavailable();
		return await StartOwnedAsync(operation, body, cancellationToken, provenance);
	}

	private async Task<PackageOperationExecution<T>> StartOwnedAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken, Func<CancellationToken, Task<BackupProvenance>>? provenance)
	{
		var root = await _provider.BeginRetainedSemanticFixturePackageOperationAsync(Owner, cancellationToken);
		PackageOperationExecution<T>? inner = null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			// The inner runner captures this flow's root and re-enters it in the body entry and in each
			// hook entry it admits (SemanticWikiPackageOperationScope), so their holds join this operation.
			using (SemanticFixturePackageOperation.EnterAmbient(root))
				inner = await _inner.StartAsync(operation, body, cancellationToken, provenance);
		}
		finally
		{
			if (inner is null) await root.DisposeAsync();
		}

		var settlement = SettleAsync(root, inner, cancellationToken);
		lock (_settling) _settling.Add(settlement);
		_ = settlement.ContinueWith(done => { lock (_settling) _settling.Remove(done); }, System.Threading.Tasks.TaskScheduler.Default);
		return new PackageOperationExecution<T>(settlement, inner.Observation);
	}

	/// <summary>
	/// Completes once every operation this coordinator started has joined, settled or not, and released its
	/// root. A caller whose cancelled observation returned early waits here before closing the world.
	/// </summary>
	internal async Task WhenIdleAsync()
	{
		Task[] pending;
		lock (_settling) pending = [.. _settling];
		await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
	}

	private async Task<PackageOperationOutcome<T>> SettleAsync<T>(SemanticFixturePackageOperation root,
		PackageOperationExecution<T> execution, CancellationToken cancellationToken)
	{
		try
		{
			// A failed completion still joins the observation, whose own failure the completion's supersedes.
			await ((Task)execution.Completion).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			if (!execution.Completion.IsCompletedSuccessfully)
				await ((Task)execution.Observation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			var outcome = await execution.Completion;
			var observation = await execution.Observation;
			if (outcome.Value is PackageOperationRan<T> && !cancellationToken.IsCancellationRequested && Certifies(observation))
			{
				try { await _provider.SettleSemanticFixturePackageOperationAsync(root); }
				catch (InvalidOperationException) { /* a world that cannot be settled stays UNKNOWN */ }
			}

			return outcome;
		}
		finally
		{
			await root.DisposeAsync();
		}
	}

	/// <summary>
	/// Whether the coordinator's frozen observation certifies the operation: the scheduler observes releases,
	/// the body's own entry was released as Completed, its body and every hook it admitted ran, were released
	/// and were observed, and no hook ran code. A hook that parsed softcode can have queued descendants nothing
	/// here observes, so it keeps the world UNKNOWN.
	/// </summary>
	internal static bool Certifies(PackageOperationObservation observation)
		=> observation is
		{
			Coordinator: PackageTaskObservation.Returned,
			Body: PackageTaskObservation.Returned,
			BodyAdmission: PackageAdmissionObservation.Accepted,
			SchedulerReleaseObservationSupported: true,
			BodyRelease: { Outcome: QueueOutcome.Completed },
			HooksRefused: 0,
			HookAdmissionFailures: 0,
			HooksWithoutReleaseObservation: 0,
		}
		&& observation.HooksAdmitted == observation.HooksCollected
		&& observation.HooksReleased == observation.HooksCollected
		&& observation.LifecycleOutcomes.All(entry => entry.Value == 0
			|| entry.Key is PackageLifecycleOutcome.Absent or PackageLifecycleOutcome.Empty)
		&& observation.HookQueueOutcomes.All(entry => entry.Value == 0 || entry.Key == QueueOutcome.Completed);
}
