using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class LoginFailureTrackerTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static void FailOnce(LoginFailureTracker tracker, string address, long handle)
	{
		tracker.TryBegin(address, handle, uint.MaxValue);
		tracker.Fail(handle);
		tracker.End(handle);
	}

	[Test]
	public async Task CountsEachAddressApart()
	{
		var tracker = new LoginFailureTracker(new Clock());

		FailOnce(tracker, "192.0.2.1", 1);
		FailOnce(tracker, "192.0.2.2", 2);
		tracker.TryBegin("192.0.2.1", 3, uint.MaxValue);

		await Assert.That(tracker.Fail(3)).IsEqualTo(2);
		await Assert.That(tracker.Count("192.0.2.2")).IsEqualTo(1);
		await Assert.That(tracker.Count("192.0.2.3")).IsEqualTo(0);
	}

	[Test]
	public async Task EndWithoutFailTakesTheAttemptBack()
	{
		var tracker = new LoginFailureTracker(new Clock());

		tracker.TryBegin("192.0.2.1", 1, 5);
		await Assert.That(tracker.Count("192.0.2.1")).IsEqualTo(1);

		tracker.End(1);

		await Assert.That(tracker.Count("192.0.2.1")).IsEqualTo(0);
	}

	[Test]
	public async Task AttemptsInProgressCountTowardTheLimit()
	{
		var tracker = new LoginFailureTracker(new Clock());

		await Assert.That(tracker.TryBegin("192.0.2.1", 1, 2)).IsTrue();
		await Assert.That(tracker.TryBegin("192.0.2.1", 2, 2)).IsTrue();
		await Assert.That(tracker.TryBegin("192.0.2.1", 3, 2)).IsFalse();

		tracker.End(1);

		await Assert.That(tracker.TryBegin("192.0.2.1", 3, 2)).IsTrue();
	}

	[Test]
	public async Task FailuresOlderThanTenMinutesStopCounting()
	{
		var clock = new Clock();
		var tracker = new LoginFailureTracker(clock);

		FailOnce(tracker, "192.0.2.1", 1);
		clock.Now += TimeSpan.FromMinutes(6);
		FailOnce(tracker, "192.0.2.1", 2);
		clock.Now += TimeSpan.FromMinutes(5);

		await Assert.That(tracker.Count("192.0.2.1")).IsEqualTo(1);
	}

	[Test]
	public async Task KeepsOnlyTheMostRecentFailures()
	{
		var tracker = new LoginFailureTracker(new Clock());

		for (var i = 0; i < LoginFailureTracker.Capacity + 5; i++)
		{
			FailOnce(tracker, "192.0.2.1", i);
		}

		await Assert.That(tracker.Count("192.0.2.1")).IsEqualTo(LoginFailureTracker.Capacity);
	}
}
