using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Tests.ConnectionServer.TestSchedulers;

namespace SharpMUSH.Tests.ConnectionServer;

public class DetachedSessionTrackerTests
{
	[Test]
	public async Task Detach_then_grace_expiry_fires_onGraceExpired_once()
	{
		var sched = new ManualScheduler();
		var tracker = new DetachedSessionTracker(sched);
		var fired = 0;
		tracker.Detach(7, () => { fired++; return Task.CompletedTask; }, TimeSpan.FromSeconds(120));

		await Assert.That(tracker.IsDetached(7)).IsTrue();
		await sched.Captured!(); // simulate grace expiry
		await Assert.That(fired).IsEqualTo(1);
		await Assert.That(tracker.IsDetached(7)).IsFalse();
	}

	[Test]
	public async Task Reattach_cancels_the_grace_timer()
	{
		var sched = new ManualScheduler();
		var tracker = new DetachedSessionTracker(sched);
		tracker.Detach(7, () => Task.CompletedTask, TimeSpan.FromSeconds(120));

		var was = tracker.Reattach(7);

		await Assert.That(was).IsTrue();
		await Assert.That(sched.Disposed).IsTrue();       // timer cancelled
		await Assert.That(tracker.IsDetached(7)).IsFalse();
	}

	[Test]
	public async Task Reattach_of_unknown_handle_returns_false()
	{
		var tracker = new DetachedSessionTracker(new ManualScheduler());
		await Assert.That(tracker.Reattach(99)).IsFalse();
	}

	[Test]
	public async Task Cancelled_grace_does_not_fire_after_reattach()
	{
		var sched = new ManualScheduler();
		var tracker = new DetachedSessionTracker(sched);
		var fired = 0;
		tracker.Detach(7, () => { fired++; return Task.CompletedTask; }, TimeSpan.FromSeconds(120));
		tracker.Reattach(7);

		await sched.Captured!(); // a late timer callback must be a no-op now

		await Assert.That(fired).IsEqualTo(0);
	}

	[Test]
	public async Task Fire_routes_a_synchronous_throw_to_onFault()
	{
		Exception? observed = null;
		var boom = new InvalidOperationException("sync boom");

		TimerGraceScheduler.Fire(() => throw boom, ex => observed = ex);

		await Assert.That(observed).IsEqualTo(boom);
	}

	[Test]
	public async Task Fire_observes_an_async_fault_instead_of_leaving_it_unobserved()
	{
		Exception? observed = null;
		var boom = new InvalidOperationException("async boom");

		// A faulted Task returned by the action must be observed (and routed), not dropped via `_ = action()`.
		TimerGraceScheduler.Fire(() => Task.FromException(boom), ex => observed = ex);

		await Assert.That(observed).IsNotNull();
		await Assert.That(observed!.Message).IsEqualTo("async boom");
	}

	[Test]
	public async Task Fire_does_not_invoke_onFault_on_success()
	{
		var faulted = false;

		TimerGraceScheduler.Fire(() => Task.CompletedTask, _ => faulted = true);

		await Assert.That(faulted).IsFalse();
	}
}
