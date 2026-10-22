using System.Threading.Channels;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

public partial class PackageOperationLifetimeTests
{
	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task ObservedBodyReleasePrecedesHookAdmissionAndCoordinatorCompletion(bool fail, bool hook)
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler(observed: true);
		var runner = CreateRunner(scheduler);
		var error = new IOException("Synthetic postcommit body failure");
		var context = new ReleaseContinuationContext();
		var opening = context.Run(() => runner.StartAsync("apply", _ =>
		{
			if (hook && !PackageLifecycleDeferral.TryDefer("#123", "AINSTALL")) throw new InvalidOperationException("Not collecting");
			return fail ? Task.FromException<bool>(error) : Task.FromResult(true);
		}));
		var owned = await opening.WaitAsync(deadline.Token);
		var entry = await scheduler.TakeAsync(deadline.Token);
		Task? later = null;
		try
		{
			await Assert.That(scheduler.ObservedAdmissionCount).IsEqualTo(1)
				.Because("the actual scheduler's release capability must be used when it is available");
			// Body publication is deliberately separate from the actual release notification.
			await entry.Action().AsTask().WaitAsync(deadline.Token);
			await context.PumpOneTurnAsync(deadline.Token);
			await Assert.That(owned.Completion.IsCompleted).IsFalse();
			later = runner.RunAsync("next", _ => Task.FromResult(true));
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(1)
				.Because("neither the committed hook nor later operation can precede actual body release");
		}
		finally
		{
			using var cleanup = new CancellationTokenSource(TestDeadline);
			// On a red implementation the test fails before invoking the body; cleanup still owns it.
			if (scheduler.ObservedAdmissionCount == 0) await entry.Action().AsTask().WaitAsync(cleanup.Token);
			entry.OnReleased?.Invoke();
			try
			{
				if (hook)
				{
					await context.PumpUntilAsync(() => scheduler.AdmissionCount >= 2, cleanup.Token);
					var hookEntry = await scheduler.TakeAsync(cleanup.Token);
					try
					{
						await hookEntry.Action().AsTask().WaitAsync(cleanup.Token);
						context.DrainReady();
						await Assert.That(owned.Completion.IsCompleted).IsFalse();
						await Assert.That(scheduler.ObservedAdmissionCount).IsEqualTo(2);
					}
					finally { hookEntry.OnReleased?.Invoke(); }
				}
			}
			finally
			{
				await context.PumpUntilAsync(() => owned.Completion.IsCompleted, cleanup.Token);
				context.DrainReady();
				await ObserveSettledAsync(owned.Completion, cleanup.Token);
				if (later is not null)
				{
					await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
					await ObserveSettledAsync(later, cleanup.Token);
				}
			}
		}
		if (fail)
		{
			Exception? observed = null;
			try { await owned.Completion; }
			catch (Exception failure) { observed = failure; }
			await Assert.That(ReferenceEquals(observed, error)).IsTrue();
		}
		else await Assert.That((await owned.Completion).Expect<PackageOperationRan<bool>>().Result).IsTrue();
	}
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task ObservedPreBodyFailureStillJoinsActualRelease(int mode)
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var request = new CancellationTokenSource();
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		var scheduler = new ControlledScheduler(observed: true);
		var backups = Substitute.For<IWorldBackupService>();
		backups.IsSupported.Returns(true);
		backups.PackageOperationKeep.Returns(1);
		var backupError = new IOException("Synthetic backup failure before the package body");
		backups.CreateBeforePackageOperationAsync(Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>())
			.Returns(_ => ValueTask.FromException<Result<WorldBackup>>(backupError));
		var runner = CreateRunner(scheduler, backupService: backups);
		var context = new ReleaseContinuationContext();
		var ran = false;
		var owned = await context.Run(() => runner.StartAsync("apply", _ => { ran = true; return Task.FromResult(true); },
			request.Token, _ => mode == 1 ? Task.FromCanceled<BackupProvenance>(cancelled.Token)
				: Task.FromResult(new BackupProvenance("pre-package")))).WaitAsync(deadline.Token);
		var entry = await scheduler.TakeAsync(deadline.Token);
		if (mode == 0) request.Cancel();
		try
		{
			await entry.Action().AsTask().WaitAsync(deadline.Token);
			await context.PumpOneTurnAsync(deadline.Token);
			await Assert.That(owned.Completion.IsCompleted).IsFalse();
			await Assert.That(ran).IsFalse();
		}
		finally
		{
			entry.OnReleased?.Invoke();
			using var cleanup = new CancellationTokenSource(TestDeadline);
			await context.PumpUntilAsync(() => owned.Completion.IsCompleted, cleanup.Token);
			context.DrainReady();
			await ObserveSettledAsync(owned.Completion, cleanup.Token);
		}
		Exception? observed = null;
		try { await owned.Completion; }
		catch (Exception error) { observed = error; }
		await Assert.That(mode == 2 ? ReferenceEquals(observed, backupError)
			: observed is OperationCanceledException cancellation && cancellation.CancellationToken == (mode == 1 ? cancelled.Token : request.Token)).IsTrue();
		var report = await owned.Observation.WaitAsync(deadline.Token);
		await Assert.That(report.Body).IsEqualTo(PackageTaskObservation.NotRun);
		await Assert.That(report.BodyAdmission).IsEqualTo(PackageAdmissionObservation.Accepted);
		await Assert.That(report.BodyRelease is not null).IsTrue();
		await Assert.That(report.Coordinator).IsEqualTo(mode == 2 ? PackageTaskObservation.Faulted : PackageTaskObservation.Cancelled);

	}

	[Test]
	public async Task ObservedRefusalNeedsNoReleaseAndFreesSequencingForTheNextOperation()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler(observed: true, refuse: true);
		var runner = CreateRunner(scheduler);
		var ran = false;
		for (var index = 0; index < 2; index++)
		{
			var result = await runner.RunAsync("apply", _ => { ran = true; return Task.FromResult(true); }).WaitAsync(deadline.Token);
			result.Expect<PackageOperationRefused>();
		}
		await Assert.That(scheduler.ObservedAdmissionCount).IsEqualTo(2);
		await Assert.That(ran).IsFalse();
	}

	/// <summary>Only the coordinator's continuations run here; all its external signals are test-owned.</summary>
	private sealed class ReleaseContinuationContext : SynchronizationContext
	{
		private readonly Channel<Action> _pending = Channel.CreateUnbounded<Action>();
		public override void Post(SendOrPostCallback callback, object? state)
		{
			if (!_pending.Writer.TryWrite(() => callback(state))) throw new InvalidOperationException("Continuation queue closed");
		}
		public T Run<T>(Func<T> action)
		{
			var previous = Current;
			try { SetSynchronizationContext(this); return action(); }
			finally { SetSynchronizationContext(previous); }
		}
		public void DrainReady()
		{
			while (_pending.Reader.TryRead(out var callback)) Run(() => { callback(); return true; });
		}
		public async Task PumpOneTurnAsync(CancellationToken token)
		{
			var callback = await _pending.Reader.ReadAsync(token);
			Run(() => { callback(); return true; });
			DrainReady();
		}
		public async Task PumpUntilAsync(Func<bool> complete, CancellationToken token)
		{
			while (!complete()) await PumpOneTurnAsync(token);
		}
	}

}
