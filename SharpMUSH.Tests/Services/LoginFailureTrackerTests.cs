using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class LoginFailureTrackerTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Test]
	public async Task CountsEachAddressApart()
	{
		var tracker = new LoginFailureTracker(new Clock());

		tracker.Record("192.0.2.1");
		tracker.Record("192.0.2.2");

		await Assert.That(tracker.Record("192.0.2.1")).IsEqualTo(2);
		await Assert.That(tracker.Count("192.0.2.2")).IsEqualTo(1);
		await Assert.That(tracker.Count("192.0.2.3")).IsEqualTo(0);
	}

	[Test]
	public async Task FailuresOlderThanTenMinutesStopCounting()
	{
		var clock = new Clock();
		var tracker = new LoginFailureTracker(clock);

		tracker.Record("192.0.2.1");
		clock.Now += TimeSpan.FromMinutes(6);
		tracker.Record("192.0.2.1");
		clock.Now += TimeSpan.FromMinutes(5);

		await Assert.That(tracker.Count("192.0.2.1")).IsEqualTo(1);
	}

	[Test]
	public async Task KeepsOnlyTheMostRecentFailures()
	{
		var tracker = new LoginFailureTracker(new Clock());

		for (var i = 0; i < LoginFailureTracker.Capacity + 5; i++)
		{
			tracker.Record("192.0.2.1");
		}

		await Assert.That(tracker.Count("192.0.2.1")).IsEqualTo(LoginFailureTracker.Capacity);
	}
}
