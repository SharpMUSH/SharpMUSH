using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageOperationRunner(
	ITaskScheduler scheduler,
	IPackageOperationGate gate,
	IWorldBackupService backups,
	IPackageLifecycleRunner lifecycle,
	ILogger<PackageOperationRunner> logger,
	IPackageOperationScope? scope = null) : IPackageOperationRunner, IPackageOperationLifetime
{
	/// <summary>The queue group package operations and their lifecycle hooks are admitted under.</summary>
	public const string QueueGroup = "package";

	/// <summary>
	/// One portal operation at a time, from its admission until its lifecycle entries are released. A hook
	/// reads its attribute when it runs, so an operation admitted ahead of it would run first and could
	/// change or remove what the hook was scheduled to run.
	/// </summary>
	private readonly SemaphoreSlim _sequence = new(1, 1);

	// Caller observation can end before settlement. Retain and observe the coordinator until all
	// scheduler-owned work has settled; it never executes mutation outside the scheduler consumer.
	private readonly HashSet<Task> _settlements = [];

	/// <summary>What the operation's entry hands back: its outcome, and the hooks it held back.</summary>
	private sealed record Finished<T>(Task<PackageOperationOutcome<T>> Completion, IReadOnlyList<PackageLifecycleHook> Hooks)
	{
		internal Finished(PackageOperationOutcome<T> outcome, IReadOnlyList<PackageLifecycleHook> hooks)
			: this(Task.FromResult(outcome), hooks) { }
	}

	/// <inheritdoc />
	public async Task<PackageOperationOutcome<T>> RunAsync<T>(
		string operation,
		Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
	{
		var execution = await StartAsync(operation, body, cancellationToken, provenance);
		return await execution.Completion.WaitAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<PackageOperationExecution<T>> StartAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
	{
		await _sequence.WaitAsync(cancellationToken);
		var observation = new TaskCompletionSource<PackageOperationObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
		var report = new PackageOperationObservationBuilder(scheduler is IQueueReleaseObserver);
		var settlement = SettleAsync(operation, body, cancellationToken, provenance, report, observation);
		lock (_settlements) _settlements.Add(settlement);
		_ = ObserveSettlementAsync(settlement, operation);
		return new PackageOperationExecution<T>(settlement, observation.Task);
	}

	private async Task ObserveSettlementAsync(Task settlement, string operation)
	{
		try
		{
			await settlement;
		}
		catch (OperationCanceledException)
		{
			// Cancellation reached the operation; its unwind has now settled.
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Package {Operation} settlement failed.", operation);
		}
		finally
		{
			lock (_settlements) _settlements.Remove(settlement);
		}
	}

	private async Task<PackageOperationOutcome<T>> SettleAsync<T>(
		string operation, Func<CancellationToken, Task<T>> body, CancellationToken cancellationToken,
		Func<CancellationToken, Task<BackupProvenance>>? provenance, PackageOperationObservationBuilder report,
		TaskCompletionSource<PackageOperationObservation> observation)
	{
		var status = PackageTaskObservation.Unknown;
		try
		{
			// Captured in the starting flow, which this runs in until its first await; each queue entry
			// the operation owns re-enters it. A capture that fails fails this operation and still
			// releases the sequence.
			var flow = scope?.Capture();
			var result = await RunInSequenceAsync(operation, body, cancellationToken, provenance, report, flow);
			status = PackageTaskObservation.Returned;
			return result;
		}
		catch (OperationCanceledException) { status = PackageTaskObservation.Cancelled; throw; }
		catch (Exception) { status = PackageTaskObservation.Faulted; throw; }
		finally
		{
			try { observation.TrySetResult(report.Snapshot(status)); }
			finally { _sequence.Release(); }
		}
	}

	private async Task<PackageOperationOutcome<T>> RunInSequenceAsync<T>(
		string operation,
		Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken, Func<CancellationToken, Task<BackupProvenance>>? provenance,
		PackageOperationObservationBuilder report, object? flow)
	{
		var completion = new TaskCompletionSource<Finished<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		// Admitted like an HTTP handler command: it arrived on a socket, not from an object, so it is
		// charged to no owner's quota (AdmitSocketWork), and the queue's single consumer runs it with
		// nothing else beside it. It is not softcode, so queue_entry_cpu_time does not time it.
		QueueAdmissionResult admission;
		try
		{
			admission = await AdmitReleasedWorkAsync(async () =>
			{
				// A cancelled request which never started must not mutate. Still settle the owned
				// coordinator so the sequence can be released when the queue skips its body.
				if (cancellationToken.IsCancellationRequested)
				{
					completion.TrySetCanceled(cancellationToken);
					return null;
				}
				using var entered = scope?.Enter(flow);
				var execution = ExecuteAsync(operation, body, cancellationToken, provenance, report);
				// The outcome, a fault included, belongs to the request waiting on it, not to the queue.
				await ((Task)execution).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
				completion.TrySetFromTask(execution);
				return null;
			}, $"package-{operation}", QueueGroup,
			// An entry halted before it ran, or dropped at shutdown, still answers the request.
			onReleased: release =>
			{
				report.BodyReleased(release);
				completion.TrySetResult(new Finished<T>(
					new PackageOperationRefused($"The package {operation} was halted before it ran; nothing was changed."), []));
				released.TrySetResult();
			}, timed: false);
		}
		catch (Exception) { report.BodyAdmissionFailed(); throw; }
		report.BodyAdmission(admission);
		if (!admission.Accepted)
		{
			logger.LogWarning("Package {Operation} refused by the queue: {Reason}.", operation, admission.Reason);
			return new PackageOperationRefused(
				$"The queue refused the package {operation} ({admission.Reason}); nothing was changed.");
		}

		// Caller observation is cancellable, but this coordinator must join the body (including
		// compensation) and every committed hook after the caller has abandoned its observation.
		Finished<T> finished;
		try { finished = await completion.Task; }
		finally
		{
			// Publication can precede release, including when backup/provenance or the gate faults.
			// No hook or later operation may overtake the body reservation's actual release.
			await released.Task;
		}
		try { await RunHooksAsync(finished.Hooks, report, flow); }
		catch (Exception exception) when (finished.Completion.IsFaulted || finished.Completion.IsCanceled)
		{
			// Join hooks first, and preserve the original body failure/cancellation if their admission
			// also failed. Both failures stay observed; a hook failure cannot turn the body into success.
			logger.LogError(exception, "Package {Operation} hook admission failed after its body failed.", operation);
		}
		return await finished.Completion;
	}

	// The optional observer signals after reservation removal and CTS disposal. Third-party schedulers
	// retain their existing release callback contract; neither path certifies business/hook success.
	private ValueTask<QueueAdmissionResult> AdmitReleasedWorkAsync(Func<ValueTask<CallState?>> action,
		string trigger, string group, Action<QueueEntryRelease?> onReleased, bool timed = true)
		=> scheduler is IQueueReleaseObserver observed
			? observed.AdmitObservedSocketWork(action, trigger, group, release => onReleased(release), timed)
			: scheduler.AdmitSocketWork(action, trigger, group, () => onReleased(null));

	/// <summary>The operation's queue entry: the backup and then the operation, in one hold of the gate.</summary>
	private async Task<Finished<T>> ExecuteAsync<T>(
		string operation, Func<CancellationToken, Task<T>> body, CancellationToken cancellationToken,
		Func<CancellationToken, Task<BackupProvenance>>? provenance, PackageOperationObservationBuilder report)
	{
		// No softcode deadline, whichever scheduler admitted the entry. Halt/shutdown still owns the
		// entry, and caller cancellation still asks the operation to unwind. Await that unwind before
		// releasing it.
		using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
			ExecutionBudget.Current?.CancelledBy ?? CancellationToken.None);
		cancellationToken = lifetime.Token;
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellationToken);
		using var scope = budget.Enter();

		// The operation takes the gate itself; holding it across the backup as well means no other
		// package operation lands between the copy and the operation it is the restore point for.
		return await gate.RunAsync(async () =>
		{
			WorldBackup? backup = null;
			if (backups is { IsSupported: true, PackageOperationKeep: > 0 })
			{
				BackupProvenance context;
				try
				{
					context = provenance is null
						? new BackupProvenance("pre-package", Operation: operation)
						: await provenance(cancellationToken);
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					logger.LogError(exception, "The provenance before a package {Operation} could not be resolved.", operation);
					return new Finished<T>(new PackageOperationRefused(
						$"The package {operation} was refused because its backup provenance could not be resolved; nothing was changed."), []);
				}
				switch (await backups.CreateBeforePackageOperationAsync(context, cancellationToken))
				{
					case WorldBackup written:
						backup = written;
						logger.LogInformation("World backed up to {Path} before a package {Operation}.", written.Path, operation);
						break;
					case Error<string> error:
						logger.LogError("The backup before a package {Operation} failed: {Error}", operation, error.Value);
						return new Finished<T>(new PackageOperationRefused(
							$"The automatic backup before this package {operation} failed ({error.Value}); nothing was changed. "
							+ "Fix the backup, or set SHARPMUSH_BACKUP_PACKAGE_KEEP=0 to run package operations without one."), []);
				}
			}

			var captured = await PackageLifecycleDeferral.CaptureCompletionAsync(() => body(cancellationToken));
			report.BodyCompleted(captured.Completion, captured.Hooks.Count);
			return new Finished<T>(BodyOutcomeAsync(captured.Completion, backup), captured.Hooks);
		}, cancellationToken);
	}

	private static async Task<PackageOperationOutcome<T>> BodyOutcomeAsync<T>(Task<T> completion, WorldBackup? backup)
		=> new PackageOperationRan<T>(await completion, backup);

	/// <summary>
	/// Runs each held-back lifecycle hook as a queue entry of its own, in order, and waits for them.
	/// The operation's entry has finished by now, so they follow it rather than run inside it, and each
	/// gets the time budget any queue entry gets.
	/// </summary>
	private async Task RunHooksAsync(IReadOnlyList<PackageLifecycleHook> hooks, PackageOperationObservationBuilder report, object? flow)
	{
		var pending = new List<Task>(hooks.Count);
		try
		{
			foreach (var hook in hooks)
			{
				var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var outcome = (int)PackageLifecycleOutcome.Unknown;
				var releaseRecorded = 0;
				QueueAdmissionResult admission;
				try
				{
					admission = await AdmitReleasedWorkAsync(async () =>
					{
						using var entered = scope?.Enter(flow);
						try
						{
							if (lifecycle is IPackageLifecycleObserver observer)
								Volatile.Write(ref outcome, (int)await observer.RunObservedLifecycleAsync(hook.ObjId, hook.Attribute, ExecutionBudget.CurrentToken));
							else await lifecycle.RunLifecycleAsync(hook.ObjId, hook.Attribute, ExecutionBudget.CurrentToken);
						}
						catch (OperationCanceledException) { Volatile.Write(ref outcome, (int)PackageLifecycleOutcome.Cancelled); throw; }
						catch (Exception) { Volatile.Write(ref outcome, (int)PackageLifecycleOutcome.Failed); throw; }
						return null;
					}, $"package-{hook.Attribute.ToLowerInvariant()}", QueueGroup,
						onReleased: release =>
						{
							if (Interlocked.Exchange(ref releaseRecorded, 1) != 0) return;
							report.HookReleased((PackageLifecycleOutcome)Volatile.Read(ref outcome), release);
							done.TrySetResult();
						});
				}
				catch (Exception) { report.HookAdmissionFailed(); throw; }
				report.HookAdmission(admission);
				if (!admission.Accepted)
				{
					logger.LogWarning("Package lifecycle {Attribute} on {ObjId} refused by the queue: {Reason}.",
						hook.Attribute, hook.ObjId, admission.Reason);
					continue;
				}

				pending.Add(done.Task);
			}
		}
		finally
		{
			// Even a later admission failure cannot abandon already admitted hook entries.
			await Task.WhenAll(pending);
		}
	}
}
