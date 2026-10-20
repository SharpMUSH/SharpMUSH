using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

public partial class PackageOperationLifetimeTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task BodyFailureAfterCommittedHookDrainsHookBeforeAdmittingTheNextOperation(bool cancelled)
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var bodyCancellation = new CancellationTokenSource();
		bodyCancellation.Cancel();
		var scheduler = new ControlledScheduler();
		var lifecycle = Substitute.For<IPackageLifecycleRunner>();
		var entered = Signal();
		var release = Signal();
		lifecycle.RunLifecycleAsync("#123", "AINSTALL", Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			entered.TrySetResult();
			await release.Task;
		});
		var runner = CreateRunner(scheduler, lifecycle);
		var error = new IOException("Body failed after a previously committed step");
		var operation = runner.RunAsync("apply", _ =>
		{
			if (!PackageLifecycleDeferral.TryDefer("#123", "AINSTALL")) throw new InvalidOperationException("Not collecting");
			return cancelled ? Task.FromCanceled<bool>(bodyCancellation.Token) : Task.FromException<bool>(error);
		});
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		var later = runner.RunAsync("next", _ => Task.FromResult(true));
		ControlledEntry? following = null;
		Task? hookExecution = null;
		try
		{
			following = await scheduler.TakeAsync(deadline.Token);
			await Assert.That(following.Trigger).IsEqualTo("package-ainstall");
			hookExecution = following.RunAsync(CancellationToken.None);
			await entered.Task.WaitAsync(deadline.Token);
			await Assert.That(operation.IsCompleted).IsFalse();
			await Assert.That(later.IsCompleted).IsFalse();
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(2);
		}
		finally
		{
			release.TrySetResult();
			using var cleanup = new CancellationTokenSource(TestDeadline);
			if (following is not null)
				await ObserveSettledAsync(hookExecution ?? following.RunAsync(CancellationToken.None), cleanup.Token);
			if (following?.Trigger == "package-ainstall")
				await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
			await ObserveSettledAsync(operation, cleanup.Token);
			await ObserveSettledAsync(later, cleanup.Token);
		}
		Exception? observed = null;
		try { await operation; }
		catch (Exception failure) { observed = failure; }
		await Assert.That(cancelled
			? observed is OperationCanceledException cancellation && cancellation.CancellationToken == bodyCancellation.Token
			: ReferenceEquals(observed, error)).IsTrue();
	}
	[Test]
	public async Task LaterHookAdmissionFailureJoinsEarlierHookAndPreservesSynchronousBodyFailure()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler("package-aupdate");
		var lifecycle = Substitute.For<IPackageLifecycleRunner>();
		var entered = Signal();
		var release = Signal();
		lifecycle.RunLifecycleAsync("#123", "AINSTALL", Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			entered.TrySetResult();
			await release.Task;
		});
		var runner = CreateRunner(scheduler, lifecycle);
		var error = new IOException("Synchronous body failure after committed steps");
		var operation = runner.RunAsync<bool>("apply", _ =>
		{
			if (!PackageLifecycleDeferral.TryDefer("#123", "AINSTALL")
				|| !PackageLifecycleDeferral.TryDefer("#123", "AUPDATE"))
				throw new InvalidOperationException("Not collecting");
			throw error;
		});
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		var hook = await scheduler.TakeAsync(deadline.Token);
		await scheduler.AdmissionFailure.Task.WaitAsync(deadline.Token);
		var hookExecution = hook.RunAsync(CancellationToken.None);
		var later = runner.RunAsync("next", _ => Task.FromResult(true));
		try
		{
			await entered.Task.WaitAsync(deadline.Token);
			await Assert.That(operation.IsCompleted).IsFalse();
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(2);
		}
		finally
		{
			release.TrySetResult();
			using var cleanup = new CancellationTokenSource(TestDeadline);
			await ObserveSettledAsync(hookExecution, cleanup.Token);
			await ObserveSettledAsync(operation, cleanup.Token);
			await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
			await ObserveSettledAsync(later, cleanup.Token);
		}
		Exception? observed = null;
		try { await operation; }
		catch (Exception failure) { observed = failure; }
		await Assert.That(ReferenceEquals(observed, error)).IsTrue();
	}

	[Test]
	public async Task ActualCompletionSurvivesCancelledObservationAndReportsLaterBodyResult()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		using var requestCancellation = new CancellationTokenSource();
		var scheduler = new ControlledScheduler();
		var runner = CreateRunner(scheduler);
		var entered = Signal();
		var release = Signal();
		var owned = await runner.StartAsync("apply", async _ =>
		{
			entered.TrySetResult();
			await release.Task;
			return true;
		}, requestCancellation.Token);
		var execution = (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None);
		Task? later = null;
		try
		{
			await entered.Task.WaitAsync(deadline.Token);
			requestCancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () =>
				await owned.Completion.WaitAsync(requestCancellation.Token));
			await Assert.That(execution.IsCompleted).IsFalse();
			later = runner.RunAsync("next", _ => Task.FromResult(true));
			await Assert.That(scheduler.AdmissionCount).IsEqualTo(1);
		}
		finally
		{
			release.TrySetResult();
			using var cleanup = new CancellationTokenSource(TestDeadline);
			await ObserveSettledAsync(execution, cleanup.Token);
			await ObserveSettledAsync(owned.Completion, cleanup.Token);
			if (later is not null)
			{
				await (await scheduler.TakeAsync(cleanup.Token)).RunAsync(CancellationToken.None).WaitAsync(cleanup.Token);
				await ObserveSettledAsync(later, cleanup.Token);
			}
		}
		await Assert.That((await owned.Completion).Expect<SharpMUSH.Library.DiscriminatedUnions.PackageOperationRan<bool>>().Result).IsTrue();
	}

	[Test]
	public async Task CancelBeforeStartingReturnsNoHandleAndAdmitsNothing()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var scheduler = new ControlledScheduler();
		var runner = CreateRunner(scheduler);
		var entered = false;
		await Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await runner.StartAsync("apply", _ => { entered = true; return Task.FromResult(true); }, cancellation.Token));
		await Assert.That(entered || scheduler.AdmissionCount != 0).IsFalse();
	}

}
