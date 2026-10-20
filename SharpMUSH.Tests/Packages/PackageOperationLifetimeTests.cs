using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Tests the runner's ownership of work independently of database/provider behavior.
/// Deadlines guard hangs; neither assertion depends on elapsed time.
/// </summary>
public partial class PackageOperationLifetimeTests
{
	private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);

	[Test]
	public async Task EntryCancellation_ReachesTheRunningPackageBody()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var entryCancellation = new CancellationTokenSource();
		var scheduler = new ControlledScheduler();
		var runner = CreateRunner(scheduler);
		var bodyEntered = Signal();
		var releaseBody = Signal();
		var compensationEntered = Signal();
		var releaseCompensation = Signal();
		var receivedToken = CancellationToken.None;
		Task? execution = null;
		var operation = runner.RunAsync("apply", async token =>
		{
			receivedToken = token;
			bodyEntered.TrySetResult();
			try
			{
				await releaseBody.Task.WaitAsync(token);
				return true;
			}
			finally
			{
				compensationEntered.TrySetResult();
				await releaseCompensation.Task;
			}
		});

		try
		{
			var entry = await scheduler.TakeAsync(deadline.Token);
			execution = entry.RunAsync(entryCancellation.Token);
			await bodyEntered.Task.WaitAsync(deadline.Token);

			// This is the external cancellation of an actual ambient ExecutionBudget,
			// as opposed to expiry of a softcode timer. Cancel synchronously propagates
			// through linked tokens, so no wait or scheduling assumption is needed.
			entryCancellation.Cancel();
			await Assert.That(receivedToken.IsCancellationRequested).IsTrue()
				.Because("halt and shutdown must reach the operation that still owns the queue entry");
			await compensationEntered.Task.WaitAsync(deadline.Token);
			await Assert.That(execution.IsCompleted).IsFalse();
		}
		finally
		{
			// Also releases a body which did not observe entry cancellation (the RED).
			releaseBody.TrySetResult();
			releaseCompensation.TrySetResult();
			using var cleanup = new CancellationTokenSource(TestDeadline);
			if (execution is not null) await ObserveSettledAsync(execution, cleanup.Token);
			else await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
			await ObserveSettledAsync(operation, cleanup.Token);
		}
	}

	[Test]
	public async Task AbandonedRequest_DoesNotAdmitALaterOperationBeforeCompensationSettles()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var requestCancellation = new CancellationTokenSource();
		var scheduler = new ControlledScheduler();
		var runner = CreateRunner(scheduler);
		var bodyEntered = Signal();
		var releaseBody = Signal();
		var compensationEntered = Signal();
		var releaseCompensation = Signal();
		Task? execution = null;
		Task? laterOperation = null;
		var operation = runner.RunAsync("apply", async token =>
		{
			bodyEntered.TrySetResult();
			try
			{
				await releaseBody.Task.WaitAsync(token);
				return true;
			}
			finally
			{
				compensationEntered.TrySetResult();
				await releaseCompensation.Task;
			}
		}, requestCancellation.Token);

		try
		{
			var entry = await scheduler.TakeAsync(deadline.Token);
			execution = entry.RunAsync(CancellationToken.None);
			await bodyEntered.Task.WaitAsync(deadline.Token);
			requestCancellation.Cancel();
			await compensationEntered.Task.WaitAsync(deadline.Token);

			// The caller may finish observing cancellation while its owned mutation
			// unwinds. Await it here so the assertion cannot race the caller's finally.
			await Assert.ThrowsAsync<OperationCanceledException>(async () =>
				await operation.WaitAsync(deadline.Token));
			deadline.Token.ThrowIfCancellationRequested();
			await Assert.That(execution.IsCompleted).IsFalse();

			// RunAsync reaches admission synchronously if _sequence was released.
			// Otherwise it waits on that semaphore. There is no worker-thread race.
			laterOperation = runner.RunAsync("apply", _ => Task.FromResult(true));
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(1)
				.Because("the first operation still owns sequencing until its compensation settles");
		}
		finally
		{
			releaseBody.TrySetResult();
			releaseCompensation.TrySetResult();
			using var cleanup = new CancellationTokenSource(TestDeadline);
			if (execution is not null) await ObserveSettledAsync(execution, cleanup.Token);
			else await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
			await ObserveSettledAsync(operation, cleanup.Token);
			if (laterOperation is not null)
			{
				var laterEntry = await scheduler.TakeAsync(cleanup.Token);
				await laterEntry.RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
				await ObserveSettledAsync(laterOperation, cleanup.Token);
			}
		}
	}

	[Test]
	public async Task CancelAfterCommit_PreservesHooksAndSequenceUntilTheirEntriesRelease()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var requestCancellation = new CancellationTokenSource();
		using var hookCancellation = new CancellationTokenSource();
		var scheduler = new ControlledScheduler();
		var lifecycle = Substitute.For<IPackageLifecycleRunner>();
		var hookEntered = Signal();
		var releaseHook = Signal();
		var hookToken = CancellationToken.None;
		lifecycle.RunLifecycleAsync("#123", "AINSTALL", Arg.Any<CancellationToken>()).Returns(async call =>
		{
			hookToken = call.Arg<CancellationToken>();
			hookEntered.TrySetResult();
			await releaseHook.Task;
		});
		var runner = CreateRunner(scheduler, lifecycle);
		var operation = runner.RunAsync("apply", _ =>
		{
			if (!PackageLifecycleDeferral.TryDefer("#123", "AINSTALL"))
				throw new InvalidOperationException("The operation did not collect its committed hook.");
			// Synthetic commit: cancellation must not discard the successful result's hook.
			requestCancellation.Cancel();
			return Task.FromResult(true);
		}, requestCancellation.Token);
		Task? execution = null;
		Task? hookExecution = null;
		ControlledEntry? hookEntry = null;
		Task? laterOperation = null;
		try
		{
			execution = (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None);
			await execution.WaitAsync(deadline.Token);
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation.WaitAsync(deadline.Token));
			deadline.Token.ThrowIfCancellationRequested();
			hookEntry = await scheduler.TakeAsync(deadline.Token);
			laterOperation = runner.RunAsync("apply", _ => Task.FromResult(true));
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(2);
			hookExecution = hookEntry.RunAsync(hookCancellation.Token);
			await hookEntered.Task.WaitAsync(deadline.Token);
			await Assert.That(hookToken.IsCancellationRequested).IsFalse()
				.Because("committed hooks survive abandonment of the HTTP request");
			hookCancellation.Cancel();
			await Assert.That(hookToken.IsCancellationRequested).IsTrue()
				.Because("each hook still belongs to its own scheduler cancellation lifetime");
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(2);
		}
		finally
		{
			releaseHook.TrySetResult();
			using var cleanup = new CancellationTokenSource(TestDeadline);
			if (execution is not null) await ObserveSettledAsync(execution, cleanup.Token);
			await ObserveSettledAsync(operation, cleanup.Token);
			if (hookExecution is null)
				hookExecution = (hookEntry ?? await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None);
			await ObserveSettledAsync(hookExecution, cleanup.Token);
			if (laterOperation is not null)
			{
				await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
				await ObserveSettledAsync(laterOperation, cleanup.Token);
			}
		}
	}

	[Test]
	public async Task CancelBeforeEntryStarts_SkipsMutationAndThenAllowsTheNextOperation()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var requestCancellation = new CancellationTokenSource();
		var scheduler = new ControlledScheduler();
		var runner = CreateRunner(scheduler);
		var mutated = false;
		var operation = runner.RunAsync("apply", _ =>
		{
			mutated = true;
			return Task.FromResult(true);
		}, requestCancellation.Token);
		var entry = await scheduler.TakeAsync(deadline.Token);
		requestCancellation.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation.WaitAsync(deadline.Token));
		deadline.Token.ThrowIfCancellationRequested();
		var laterOperation = runner.RunAsync("apply", _ => Task.FromResult(true));
		try
		{
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(1);
		}
		finally
		{
			using var cleanup = new CancellationTokenSource(TestDeadline);
			await entry.RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
			await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
			await ObserveSettledAsync(laterOperation, cleanup.Token);
		}
		await Assert.That(mutated).IsFalse();
	}

	private static PackageOperationRunner CreateRunner(ControlledScheduler scheduler, IPackageLifecycleRunner? lifecycle = null, IWorldBackupService? backupService = null)
	{
		var backups = backupService ?? Substitute.For<IWorldBackupService>();
		if (backupService is null) backups.IsSupported.Returns(false);
		return new PackageOperationRunner(scheduler.Scheduler, new PackageOperationGate(), backups,
			lifecycle ?? Substitute.For<IPackageLifecycleRunner>(), NullLogger<PackageOperationRunner>.Instance);
	}

	private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Observes expected cancelled/faulted tasks but never suppresses a cleanup timeout.</summary>
	private static async Task ObserveSettledAsync(Task task, CancellationToken cancellationToken)
	{
		try
		{
			await task.WaitAsync(cancellationToken);
		}
		catch (Exception) when (task.IsCompleted)
		{
			// The test's assertion owns the outcome. Cleanup owns joining the task.
		}
	}

	private sealed class ControlledScheduler
	{
		private readonly Channel<ControlledEntry> _entries = Channel.CreateUnbounded<ControlledEntry>();
		private int _admissionCount;

		public ITaskScheduler Scheduler { get; }
		public int ObservedAdmissionCount { get; private set; }
		public int AdmissionCount => Volatile.Read(ref _admissionCount);
		public TaskCompletionSource AdmissionFailure { get; } = Signal();

		public ControlledScheduler(string? failTrigger = null, bool observed = false, bool refuse = false)
		{
			Scheduler = observed ? Substitute.For<ITaskScheduler, IQueueReleaseObserver>() : Substitute.For<ITaskScheduler>();
			if (observed)
				((IQueueReleaseObserver)Scheduler).AdmitObservedSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<QueueEntryRelease>>())
					.Returns(call =>
					{
						ObservedAdmissionCount++;
						if (refuse) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.GlobalLimit));
						var pid = Interlocked.Increment(ref _admissionCount);
						var release = call.Arg<Action<QueueEntryRelease>>();
						var entry = new ControlledEntry(call.Arg<Func<ValueTask<CallState?>>>(),
							() => release(new(pid, SharpMUSH.Library.Models.Diagnostics.QueueOutcome.Completed, null)), call.ArgAt<string>(1));
						if (!_entries.Writer.TryWrite(entry)) throw new InvalidOperationException("Test admission channel closed.");
						return ValueTask.FromResult(new QueueAdmissionResult(pid, QueueRejectionReason.None));
					});
			Scheduler.AdmitSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>())
				.Returns(call =>
				{
					if (call.ArgAt<string>(1) == failTrigger)
					{
						AdmissionFailure.TrySetResult();
						throw new IOException("Synthetic hook admission failure");
					}
					var pid = Interlocked.Increment(ref _admissionCount);
					var entry = new ControlledEntry(call.Arg<Func<ValueTask<CallState?>>>(), call.Arg<Action?>(), call.ArgAt<string>(1));
					if (!_entries.Writer.TryWrite(entry)) throw new InvalidOperationException("Test admission channel closed.");
					return ValueTask.FromResult(new QueueAdmissionResult(pid, QueueRejectionReason.None));
				});
		}

		public ValueTask<ControlledEntry> TakeAsync(CancellationToken cancellationToken) =>
			_entries.Reader.ReadAsync(cancellationToken);
	}

	private sealed record ControlledEntry(Func<ValueTask<CallState?>> Action, Action? OnReleased, string Trigger)
	{
		public async Task RunAsync(CancellationToken entryCancellation)
		{
			using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, entryCancellation);
			using var scope = budget.Enter();
			try
			{
				await Action();
			}
			finally
			{
				OnReleased?.Invoke();
			}
		}
	}
}
