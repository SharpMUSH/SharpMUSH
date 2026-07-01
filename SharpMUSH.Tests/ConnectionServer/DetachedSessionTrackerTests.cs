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
}
