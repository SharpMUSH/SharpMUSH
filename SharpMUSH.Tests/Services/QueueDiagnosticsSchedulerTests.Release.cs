using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class QueueDiagnosticsSchedulerTests
{
	private static TaskCompletionSource<QueueEntryRelease> ReleaseSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
	private static TaskCompletionSource ReleaseBarrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ObservedCallbackOutcomeDoesNotRequireDiagnosticHistory(bool fail)
	{
		await using var queue = Create(null);
		var released = ReleaseSignal();
		IQueueReleaseObserver observer = queue;
		var admitted = await observer.AdmitObservedSocketWork(() => fail
			? throw new IOException("Synthetic callback failure") : ValueTask.FromResult<CallState?>(null),
			"observed-body", "package", result => released.TrySetResult(result));
		var result = await released.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(result.Outcome).IsEqualTo(fail ? QueueOutcome.Failed : QueueOutcome.Completed);
		await Assert.That(result.Pid).IsEqualTo(admitted.Pid);
		await Assert.That(result.FailedInvocations).IsNull();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task UntimedObservedWorkIsNotChargedTheSoftcodeDeadline()
	{
		var recorder = new QueueDiagnosticsRecorder();
		await using var queue = Create(recorder, milliseconds: 1);
		var released = ReleaseSignal();
		var expired = true;
		await queue.AdmitObservedSocketWork(async () =>
		{
			// Well past the 1 ms queue_entry_cpu_time a timed entry would get.
			await Task.Delay(50);
			expired = ExecutionBudget.Current is not { IsExpired: false };
			return null;
		}, "untimed-body", "package", result => released.TrySetResult(result), timed: false);
		var result = await released.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(result.Outcome).IsEqualTo(QueueOutcome.Completed);
		await Assert.That(expired).IsFalse();
		await HistoryCount(recorder, 1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Completed);
	}

	[Test]
	public async Task UntimedObservedWorkIsStillHalted()
	{
		await using var queue = Create(null, milliseconds: 1);
		var entered = ReleaseBarrier();
		var released = ReleaseSignal();
		var admission = await queue.AdmitObservedSocketWork(async () =>
		{
			entered.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken);
			return null;
		}, "untimed-halt", "package", result => released.TrySetResult(result), timed: false);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await queue.HaltByPid(admission.Pid!.Value);
		var result = await released.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(result.Outcome).IsEqualTo(QueueOutcome.Cancelled);
	}

	[Test]
	public async Task ObservedCancellationJoinsActualUnwindBeforeRelease()
	{
		await using var queue = Create(null, milliseconds: 30000);
		var entered = ReleaseBarrier();
		var unwinding = ReleaseBarrier();
		var finish = ReleaseBarrier();
		var released = ReleaseSignal();
		var admission = await queue.AdmitObservedSocketWork(async () =>
		{
			entered.TrySetResult();
			try { await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken); }
			finally { unwinding.TrySetResult(); await finish.Task; }
			return null;
		}, "observed-cancel", "package", result => released.TrySetResult(result));
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await queue.HaltByPid(admission.Pid!.Value);
			await unwinding.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That(released.Task.IsCompleted).IsFalse();
		}
		finally { finish.TrySetResult(); }
		var result = await released.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(result.Outcome).IsEqualTo(QueueOutcome.Cancelled);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task ObservedPreStartHaltDoesNotRunTheBodyAndReleasesOnlyOnce()
	{
		await using var queue = Create(null, milliseconds: 30000);
		var entered = ReleaseBarrier();
		var finish = ReleaseBarrier();
		var released = ReleaseSignal();
		var ran = false;
		var count = 0;
		await queue.AdmitSocketWork(async () => { entered.TrySetResult(); await finish.Task; return null; }, "blocking", "package");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			var admission = await queue.AdmitObservedSocketWork(() => { ran = true; return ValueTask.FromResult<CallState?>(null); },
				"observed-halt", "package", result => { Interlocked.Increment(ref count); released.TrySetResult(result); });
			await queue.HaltByPid(admission.Pid!.Value);
			await Assert.That(released.Task.IsCompleted).IsFalse();
		}
		finally { finish.TrySetResult(); }
		await Assert.That((await released.Task.WaitAsync(TimeSpan.FromSeconds(10))).Outcome).IsEqualTo(QueueOutcome.Cancelled);
		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
		await Assert.That(ran).IsFalse();
		await Assert.That(count).IsEqualTo(1);
	}

	[Test]
	public async Task ObservedRejectionHasNoReleaseCallback()
	{
		await using var queue = Create(null, capacity: 0);
		var called = false;
		var admission = await queue.AdmitObservedSocketWork(() => ValueTask.FromResult<CallState?>(null),
			"observed-rejection", "package", _ => called = true);
		await Assert.That(admission.Accepted).IsFalse();
		await Assert.That(called).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ObservedOrLegacyReleaseCallbackFailureCannotStopTheConsumer(bool legacy)
	{
		await using var queue = Create(null);
		var first = ReleaseBarrier();
		var next = ReleaseBarrier();
		if (legacy)
			await queue.AdmitSocketWork(() => ValueTask.FromResult<CallState?>(null), "throwing-legacy-release", "package",
				() => { first.TrySetResult(); throw new IOException("Synthetic observer failure"); });
		else
			await queue.AdmitObservedSocketWork(() => ValueTask.FromResult<CallState?>(null), "throwing-observed-release", "package",
				_ => { first.TrySetResult(); throw new IOException("Synthetic observer failure"); });
		await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await queue.AdmitSocketWork(() => ValueTask.FromResult<CallState?>(null), "following-entry", "package", () => next.TrySetResult());
		await next.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task ObservedReleasePreservesExistingInvocationFailures()
	{
		var recorder = new QueueDiagnosticsRecorder();
		await using var queue = Create(recorder);
		var released = ReleaseSignal();
		await queue.AdmitObservedSocketWork(() =>
		{
			recorder.RecordInvocation(new(TelemetryInvocationKind.Command, "synthetic", 1, false));
			return ValueTask.FromResult<CallState?>(null);
		}, "observed-invocation", "package", result => released.TrySetResult(result));
		var result = await released.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(result.Outcome).IsEqualTo(QueueOutcome.InvocationFailure);
		await Assert.That(result.FailedInvocations).IsEqualTo(1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(result.Outcome);
	}

	[Test]
	public async Task ObservedExecutionLimitIsNotACompletedCallback()
	{
		await using var queue = Create(null, milliseconds: 20);
		var released = ReleaseSignal();
		await queue.AdmitObservedSocketWork(async () =>
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken);
			return null;
		}, "observed-deadline", "package", result => released.TrySetResult(result));
		await Assert.That((await released.Task.WaitAsync(TimeSpan.FromSeconds(10))).Outcome).IsEqualTo(QueueOutcome.ExecutionLimit);
	}
	[Test]
	public async Task ObservedShutdownJoinsRunningUnwindAndReleasesQueuedWorkExactlyOnce()
	{
		var queue = Create(null, milliseconds: 30000);
		var entered = ReleaseBarrier();
		var unwinding = ReleaseBarrier();
		var finish = ReleaseBarrier();
		var runningRelease = ReleaseSignal();
		var queuedRelease = ReleaseSignal();
		var runningCount = 0;
		var queuedCount = 0;
		var queuedRan = false;
		Task? stopping = null;
		try
		{
			await queue.AdmitObservedSocketWork(async () =>
			{
				entered.TrySetResult();
				try { await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken); }
				finally { unwinding.TrySetResult(); await finish.Task; }
				return null;
			}, "observed-shutdown-running", "package", result =>
			{
				Interlocked.Increment(ref runningCount);
				runningRelease.TrySetResult(result);
			});
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await queue.AdmitObservedSocketWork(() => { queuedRan = true; return ValueTask.FromResult<CallState?>(null); },
				"observed-shutdown-queued", "package", result =>
				{
					Interlocked.Increment(ref queuedCount);
					queuedRelease.TrySetResult(result);
				});
			stopping = queue.DisposeAsync().AsTask();
			await unwinding.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That(stopping.IsCompleted || runningRelease.Task.IsCompleted || queuedRelease.Task.IsCompleted).IsFalse();
			finish.TrySetResult();
			await stopping.WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That((await runningRelease.Task.WaitAsync(TimeSpan.FromSeconds(10))).Outcome).IsEqualTo(QueueOutcome.Cancelled);
			await Assert.That((await queuedRelease.Task.WaitAsync(TimeSpan.FromSeconds(10))).Outcome).IsEqualTo(QueueOutcome.Cancelled);
			await Assert.That(queuedRan).IsFalse();
			await Assert.That(runningCount == 1 && queuedCount == 1 && queue.GetQueueUsage().Total == 0).IsTrue();
		}
		finally
		{
			finish.TrySetResult();
			if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(10));
			else await queue.DisposeAsync();
		}
	}

	[Test]
	[Arguments(QueueOutcome.Failed)]
	[Arguments(QueueOutcome.Cancelled)]
	[Arguments(QueueOutcome.ExecutionLimit)]
	public async Task ObservedTerminalOutcomeTakesPrecedenceOverInvocationFailures(QueueOutcome expected)
	{
		var recorder = new QueueDiagnosticsRecorder();
		await using var queue = Create(recorder, milliseconds: expected == QueueOutcome.ExecutionLimit ? 1000u : 30000u);
		var entered = ReleaseBarrier();
		var released = ReleaseSignal();
		var admission = await queue.AdmitObservedSocketWork(async () =>
		{
			recorder.RecordInvocation(new(TelemetryInvocationKind.Command, "synthetic", 1, false));
			entered.TrySetResult();
			if (expected == QueueOutcome.Failed) throw new IOException("Synthetic callback failure after a failed invocation");
			await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken);
			return null;
		}, "observed-precedence", "package", result => released.TrySetResult(result));
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		if (expected == QueueOutcome.Cancelled) await queue.HaltByPid(admission.Pid!.Value);
		var result = await released.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(result.Outcome).IsEqualTo(expected);
		await Assert.That(result.FailedInvocations).IsEqualTo(1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(expected);
	}

	[Test]
	public async Task ObservedReleaseReturnsQuotaBeforeObserverAdmitsFollowingWork()
	{
		await using var queue = Create(null, capacity: 1);
		var nextAdmission = new TaskCompletionSource<Task<QueueAdmissionResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var nextRelease = ReleaseSignal();
		var usageAtRelease = -1;
		await queue.AdmitObservedSocketWork(() => ValueTask.FromResult<CallState?>(null), "observed-first", "package", _ =>
		{
			usageAtRelease = queue.GetQueueUsage().Total;
			nextAdmission.TrySetResult(queue.AdmitObservedSocketWork(() => ValueTask.FromResult<CallState?>(null),
				"observed-following", "package", result => nextRelease.TrySetResult(result)).AsTask());
		});
		await Assert.That((await nextAdmission.Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(10))).Accepted).IsTrue();
		await Assert.That((await nextRelease.Task.WaitAsync(TimeSpan.FromSeconds(10))).Outcome).IsEqualTo(QueueOutcome.Completed);
		await Assert.That(usageAtRelease).IsEqualTo(0);
	}

}
