using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageOperationRunner(
	ITaskScheduler scheduler,
	IPackageOperationGate gate,
	IWorldBackupService backups,
	IPackageLifecycleRunner lifecycle,
	ILogger<PackageOperationRunner> logger) : IPackageOperationRunner
{
	/// <summary>The queue group package operations and their lifecycle hooks are admitted under.</summary>
	public const string QueueGroup = "package";

	/// <summary>
	/// One portal operation at a time, from its admission until its lifecycle hooks are queued. A hook
	/// reads its attribute when it runs, so an operation admitted ahead of it would run first and could
	/// change or remove what the hook was scheduled to run.
	/// </summary>
	private readonly SemaphoreSlim _sequence = new(1, 1);

	/// <summary>What the operation's entry hands back: its outcome, and the hooks it held back.</summary>
	private sealed record Finished<T>(PackageOperationOutcome<T> Outcome, IReadOnlyList<PackageLifecycleHook> Hooks);

	/// <inheritdoc />
	public async Task<PackageOperationOutcome<T>> RunAsync<T>(
		string operation,
		Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default)
	{
		await _sequence.WaitAsync(cancellationToken);
		try
		{
			return await RunInSequenceAsync(operation, body, cancellationToken);
		}
		finally
		{
			_sequence.Release();
		}
	}

	private async Task<PackageOperationOutcome<T>> RunInSequenceAsync<T>(
		string operation,
		Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<Finished<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var abandoned = false;

		// Admitted like an HTTP handler command: it arrived on a socket, not from an object, so it is
		// charged to no owner's quota (AdmitSocketWork), and the queue's single consumer runs it with
		// nothing else beside it.
		var admission = await scheduler.AdmitSocketWork(async () =>
			{
				// A request that gave up while waiting its turn has nobody left to answer.
				if (Volatile.Read(ref abandoned)) return null;
				var execution = ExecuteAsync(operation, body, cancellationToken);
				// The outcome, a fault included, belongs to the request waiting on it, not to the queue.
				await ((Task)execution).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
				completion.TrySetFromTask(execution);
				return null;
			}, $"package-{operation}", QueueGroup,
			// An entry halted before it ran, or dropped at shutdown, still answers the request.
			onReleased: () => completion.TrySetResult(new Finished<T>(
				new PackageOperationRefused($"The package {operation} was halted before it ran; nothing was changed."), [])));
		if (!admission.Accepted)
		{
			logger.LogWarning("Package {Operation} refused by the queue: {Reason}.", operation, admission.Reason);
			return new PackageOperationRefused(
				$"The queue refused the package {operation} ({admission.Reason}); nothing was changed.");
		}

		Finished<T> finished;
		try
		{
			finished = await completion.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			Volatile.Write(ref abandoned, true);
		}

		await RunHooksAsync(finished.Hooks, cancellationToken);
		return finished.Outcome;
	}

	/// <summary>The operation's queue entry: the backup and then the operation, in one hold of the gate.</summary>
	private async Task<Finished<T>> ExecuteAsync<T>(
		string operation, Func<CancellationToken, Task<T>> body, CancellationToken cancellationToken)
	{
		// The entry's time budget is for softcode. A package operation is not softcode, and stopping it
		// part way only makes it undo what it has done, so it runs to the end.
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellationToken);
		using var scope = budget.Enter();

		// The operation takes the gate itself; holding it across the backup as well means no other
		// package operation lands between the copy and the operation it is the restore point for.
		return await gate.RunAsync(async () =>
		{
			WorldBackup? backup = null;
			if (backups is { IsSupported: true, PackageOperationKeep: > 0 })
			{
				switch (await backups.CreateBeforePackageOperationAsync(cancellationToken))
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

			var (result, hooks) = await PackageLifecycleDeferral.CollectAsync(() => body(cancellationToken));
			return new Finished<T>(new PackageOperationRan<T>(result, backup), hooks);
		}, cancellationToken);
	}

	/// <summary>
	/// Runs each held-back lifecycle hook as a queue entry of its own, in order, and waits for them.
	/// The operation's entry has finished by now, so they follow it rather than run inside it, and each
	/// gets the time budget any queue entry gets.
	/// </summary>
	private async Task RunHooksAsync(IReadOnlyList<PackageLifecycleHook> hooks, CancellationToken cancellationToken)
	{
		var pending = new List<Task>(hooks.Count);
		foreach (var hook in hooks)
		{
			var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var admission = await scheduler.AdmitSocketWork(async () =>
				{
					await lifecycle.RunLifecycleAsync(hook.ObjId, hook.Attribute, CancellationToken.None);
					return null;
				}, $"package-{hook.Attribute.ToLowerInvariant()}", QueueGroup,
				onReleased: () => done.TrySetResult());
			if (!admission.Accepted)
			{
				logger.LogWarning("Package lifecycle {Attribute} on {ObjId} refused by the queue: {Reason}.",
					hook.Attribute, hook.ObjId, admission.Reason);
				continue;
			}

			pending.Add(done.Task);
		}

		await Task.WhenAll(pending).WaitAsync(cancellationToken);
	}
}
