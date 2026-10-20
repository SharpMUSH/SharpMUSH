using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

public partial class PackageOperationLifetimeTests
{
	[Test]
	[Arguments(PackageLifecycleOutcome.Parsed)]
	[Arguments(PackageLifecycleOutcome.CommandFailure)]
	[Arguments(PackageLifecycleOutcome.Failed)]
	[Arguments(PackageLifecycleOutcome.Cancelled)]
	[Arguments(PackageLifecycleOutcome.Deferred)]
	[Arguments((PackageLifecycleOutcome)999)]
	public async Task RetainedObservationKeepsLocalVerdictsSeparateFromReturnedBusinessFailure(PackageLifecycleOutcome outcome)
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler(observed: true);
		var lifecycle = ObservedLifecycle(outcome);
		var owned = await CreateRunner(scheduler, lifecycle).StartAsync("apply", _ =>
		{
			if (!PackageLifecycleDeferral.TryDefer("#123", "AINSTALL")) throw new InvalidOperationException("Not collecting");
			return Task.FromResult(false);
		});
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		await Assert.That((await owned.Completion.WaitAsync(deadline.Token)).Expect<PackageOperationRan<bool>>().Result).IsFalse();
		var report = await owned.Observation.WaitAsync(deadline.Token);
		await Assert.That(report.Coordinator).IsEqualTo(PackageTaskObservation.Returned);
		await Assert.That(report.Body).IsEqualTo(PackageTaskObservation.Returned);
		await Assert.That(report.BodyAdmission).IsEqualTo(PackageAdmissionObservation.Accepted);
		await Assert.That(report.SchedulerReleaseObservationSupported).IsTrue();
		await Assert.That(report.BodyRelease?.Outcome).IsEqualTo(QueueOutcome.Completed);
		await Assert.That(report.HooksCollected).IsEqualTo(1);
		await Assert.That(report.HooksAdmitted).IsEqualTo(1);
		await Assert.That(report.HooksReleased).IsEqualTo(1);
		await Assert.That(report.HooksWithoutReleaseObservation).IsEqualTo(0);
		var normalized = Enum.IsDefined(outcome) ? outcome : PackageLifecycleOutcome.Unknown;
		await Assert.That(report.LifecycleOutcomes.GetValueOrDefault(normalized)).IsEqualTo(1);
		await Assert.That(report.HookQueueOutcomes.GetValueOrDefault(QueueOutcome.Completed)).IsEqualTo(1);
		await Assert.ThrowsAsync<NotSupportedException>(() =>
		{
			((IDictionary<PackageLifecycleOutcome, long>)report.LifecycleOutcomes).Add(PackageLifecycleOutcome.Unknown, 55);
			return Task.CompletedTask;
		});
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FaultedOrCancelledBodyStillPublishesJoinedHookObservation(bool cancel)
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		var scheduler = new ControlledScheduler(observed: true);
		var error = new IOException("Original body failure");
		var owned = await CreateRunner(scheduler, ObservedLifecycle(PackageLifecycleOutcome.CommandFailure)).StartAsync("apply", _ =>
		{
			if (!PackageLifecycleDeferral.TryDefer("#123", "AINSTALL")) throw new InvalidOperationException("Not collecting");
			return cancel ? Task.FromCanceled<bool>(cancelled.Token) : Task.FromException<bool>(error);
		});
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		Exception? caught = null;
		try { await owned.Completion.WaitAsync(deadline.Token); }
		catch (Exception exception) { caught = exception; }
		await Assert.That(cancel ? caught is OperationCanceledException : ReferenceEquals(caught, error)).IsTrue();
		var report = await owned.Observation.WaitAsync(deadline.Token);
		var expected = cancel ? PackageTaskObservation.Cancelled : PackageTaskObservation.Faulted;
		await Assert.That(report.Body).IsEqualTo(expected);
		await Assert.That(report.Coordinator).IsEqualTo(expected);
		await Assert.That(report.HooksReleased).IsEqualTo(1);
		await Assert.That(report.LifecycleOutcomes.GetValueOrDefault(PackageLifecycleOutcome.CommandFailure)).IsEqualTo(1);
	}

	[Test]
	public async Task UnsupportedObserversRemainUnknownAfterLegacyRelease()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler();
		var owned = await CreateRunner(scheduler).StartAsync("apply", _ =>
		{
			PackageLifecycleDeferral.TryDefer("#123", "AINSTALL");
			return Task.FromResult(true);
		});
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		await owned.Completion.WaitAsync(deadline.Token);
		var report = await owned.Observation.WaitAsync(deadline.Token);
		await Assert.That(report.SchedulerReleaseObservationSupported).IsFalse();
		await Assert.That(report.BodyRelease is null).IsTrue();
		await Assert.That(report.HooksWithoutReleaseObservation).IsEqualTo(1);
		await Assert.That(report.LifecycleOutcomes.GetValueOrDefault(PackageLifecycleOutcome.Unknown)).IsEqualTo(1);
		await Assert.That(report.HookQueueOutcomes.Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task QueueRefusalReportsBodyAndHookAdmissionSeparately(bool refuseBody)
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler(observed: true, refuse: refuseBody, refuseTrigger: "package-ainstall");
		var owned = await CreateRunner(scheduler).StartAsync("apply", _ =>
		{
			PackageLifecycleDeferral.TryDefer("#123", "AINSTALL");
			return Task.FromResult(true);
		});
		if (!refuseBody) await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		await owned.Completion.WaitAsync(deadline.Token);
		var report = await owned.Observation.WaitAsync(deadline.Token);
		await Assert.That(report.Coordinator).IsEqualTo(PackageTaskObservation.Returned);
		await Assert.That(report.Body).IsEqualTo(refuseBody ? PackageTaskObservation.NotRun : PackageTaskObservation.Returned);
		await Assert.That(report.BodyAdmission).IsEqualTo(refuseBody ? PackageAdmissionObservation.Refused : PackageAdmissionObservation.Accepted);
		await Assert.That(report.BodyRejection).IsEqualTo(refuseBody ? QueueRejectionReason.GlobalLimit : QueueRejectionReason.None);
		await Assert.That(report.HooksRefused).IsEqualTo(refuseBody ? 0 : 1);
		await Assert.That(report.HooksReleased).IsEqualTo(0);
	}

	[Test]
	public async Task LaterAdmissionFailureRetainsEarlierHookObservation()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler(failTrigger: "package-aupdate");
		var owned = await CreateRunner(scheduler, ObservedLifecycle(PackageLifecycleOutcome.Parsed)).StartAsync("apply", _ =>
		{
			PackageLifecycleDeferral.TryDefer("#123", "AINSTALL");
			PackageLifecycleDeferral.TryDefer("#123", "AUPDATE");
			return Task.FromResult(true);
		});
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		var hook = await scheduler.TakeAsync(deadline.Token);
		await scheduler.AdmissionFailure.Task.WaitAsync(deadline.Token);
		try { await Assert.That(owned.Observation.IsCompleted).IsFalse(); }
		finally { await hook.RunAsync(CancellationToken.None).WaitAsync(deadline.Token); }
		await Assert.ThrowsAsync<IOException>(async () => await owned.Completion.WaitAsync(deadline.Token));
		var report = await owned.Observation.WaitAsync(deadline.Token);
		await Assert.That(report.Coordinator).IsEqualTo(PackageTaskObservation.Faulted);
		await Assert.That(report.Body).IsEqualTo(PackageTaskObservation.Returned);
		await Assert.That(report.HooksCollected).IsEqualTo(2);
		await Assert.That(report.HooksAdmitted).IsEqualTo(1);
		await Assert.That(report.HookAdmissionFailures).IsEqualTo(1);
		await Assert.That(report.HooksReleased).IsEqualTo(1);
	}

	[Test]
	public async Task CancellingReportObservationCannotPublishBeforeActualRelease()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var cancelled = new CancellationTokenSource();
		var scheduler = new ControlledScheduler(observed: true);
		var owned = await CreateRunner(scheduler).StartAsync("apply", _ => Task.FromResult(true));
		var entry = await scheduler.TakeAsync(deadline.Token);
		try
		{
			cancelled.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await owned.Observation.WaitAsync(cancelled.Token));
			await entry.Action().AsTask().WaitAsync(deadline.Token);
			await Assert.That(owned.Observation.IsCompleted).IsFalse();
		}
		finally { entry.OnReleased?.Invoke(); }
		await owned.Completion.WaitAsync(deadline.Token);
		await Assert.That((await owned.Observation.WaitAsync(deadline.Token)).BodyRelease is not null).IsTrue();
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task SynchronousReleaseKeepsHaltedAndThrowingHookEvidence(int mode)
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = Substitute.For<ITaskScheduler, IQueueReleaseObserver>();
		var lifecycle = ObservedLifecycle(PackageLifecycleOutcome.Parsed);
		if (mode is 2 or 3)
			((IPackageLifecycleObserver)lifecycle).RunObservedLifecycleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
				.Returns(_ => mode == 2 ? Task.FromException<PackageLifecycleOutcome>(new IOException("Hook failure"))
					: Task.FromException<PackageLifecycleOutcome>(new OperationCanceledException()));
		var pid = 0L;
		((IQueueReleaseObserver)scheduler).AdmitObservedSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<QueueEntryRelease>>())
			.Returns(call =>
			{
				var hook = call.ArgAt<string>(1) == "package-ainstall";
				var queueOutcome = QueueOutcome.Completed;
				if (hook && mode is 1 or 4) queueOutcome = mode == 1 ? QueueOutcome.Halted : (QueueOutcome)999;
				else
				{
					try { call.Arg<Func<ValueTask<CallState?>>>()().AsTask().GetAwaiter().GetResult(); }
					catch (OperationCanceledException) { queueOutcome = QueueOutcome.Cancelled; }
					catch (IOException) { queueOutcome = QueueOutcome.Failed; }
				}
				var current = ++pid;
				// Explicit scheduler contract: release may precede the admission return.
				call.Arg<Action<QueueEntryRelease>>()(new(current, queueOutcome, null));
				return ValueTask.FromResult(new QueueAdmissionResult(current, QueueRejectionReason.None));
			});
		var backups = Substitute.For<IWorldBackupService>();
		backups.IsSupported.Returns(false);
		var runner = new PackageOperationRunner(scheduler, new PackageOperationGate(), backups, lifecycle, NullLogger<PackageOperationRunner>.Instance);
		var owned = await runner.StartAsync("apply", _ =>
		{
			PackageLifecycleDeferral.TryDefer("#123", "AINSTALL");
			return Task.FromResult(true);
		});
		await owned.Completion.WaitAsync(deadline.Token);
		var report = await owned.Observation.WaitAsync(deadline.Token);
		await Assert.That(report.BodyAdmission).IsEqualTo(PackageAdmissionObservation.Accepted);
		await Assert.That(report.HooksAdmitted).IsEqualTo(1);
		await Assert.That(report.HooksReleased).IsEqualTo(1);
		var local = mode switch
		{
			0 => PackageLifecycleOutcome.Parsed,
			2 => PackageLifecycleOutcome.Failed,
			3 => PackageLifecycleOutcome.Cancelled,
			_ => PackageLifecycleOutcome.Unknown
		};
		var queue = mode switch { 1 => QueueOutcome.Halted, 2 => QueueOutcome.Failed, 3 => QueueOutcome.Cancelled, _ => QueueOutcome.Completed };
		await Assert.That(report.LifecycleOutcomes.GetValueOrDefault(local)).IsEqualTo(1);
		await Assert.That(report.HooksWithoutReleaseObservation).IsEqualTo(mode == 4 ? 1 : 0);
		await Assert.That(report.HookQueueOutcomes.GetValueOrDefault(queue)).IsEqualTo(mode == 4 ? 0 : 1);
		await Assert.ThrowsAsync<NotSupportedException>(() =>
		{
			((IDictionary<QueueOutcome, long>)report.HookQueueOutcomes).Add(QueueOutcome.InvocationFailure, 99);
			return Task.CompletedTask;
		});
	}

	private static IPackageLifecycleRunner ObservedLifecycle(PackageLifecycleOutcome outcome)
	{
		var lifecycle = Substitute.For<IPackageLifecycleRunner, IPackageLifecycleObserver>();
		((IPackageLifecycleObserver)lifecycle).RunObservedLifecycleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(outcome));
		return lifecycle;
	}
}
