using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>A call's clock leaves out its arguments and the calls nested inside it.</summary>
public class InvocationClockTests
{
	/// <summary>Time that moves only when the test moves it, one millisecond per tick.</summary>
	private sealed class ManualTime : TimeProvider
	{
		// A clock reads a timestamp of 0 as not stopped yet, as Stopwatch never returns it.
		private long _now = 1;
		public override long TimestampFrequency => 1000;
		public override long GetTimestamp() => _now;
		public void Advance(long ms) => _now += ms;
	}

	[Test]
	public async Task ANestedCallPausesItsCaller()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var outer = InvocationClock.Start();
		time.Advance(1);
		var inner = InvocationClock.Start();
		time.Advance(200);
		inner.Stop();
		time.Advance(2);
		outer.Stop();

		await Assert.That(inner.OwnMilliseconds).IsEqualTo(200);
		await Assert.That(outer.OwnMilliseconds).IsEqualTo(3);
		await Assert.That(outer.InclusiveMilliseconds).IsEqualTo(203);
	}

	[Test]
	public async Task EvaluatingArgumentsPausesTheCall()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var clock = InvocationClock.Start();
		time.Advance(1);
		using (clock.Pause())
		{
			using (clock.Pause())
				time.Advance(200);
			time.Advance(200);
		}

		time.Advance(2);
		clock.Stop();

		await Assert.That(clock.OwnMilliseconds).IsEqualTo(3);
		await Assert.That(clock.InclusiveMilliseconds).IsEqualTo(403);
	}

	[Test]
	public async Task ADeferredArgumentPausesTheCallRunningIt()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var command = InvocationClock.Start();
		using (InvocationClock.PauseRunning())
			time.Advance(200);
		time.Advance(1);
		command.Stop();

		await Assert.That(command.OwnMilliseconds).IsEqualTo(1);
		using (InvocationClock.PauseRunning()) { }
	}

	[Test]
	public async Task AStoppedCallLeavesWhatFollowsToItsCaller()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var outer = InvocationClock.Start();
		var prose = InvocationClock.Start();
		prose.Stop();
		time.Advance(200);
		prose.Stop();
		outer.Stop();

		await Assert.That(prose.OwnMilliseconds).IsEqualTo(0);
		await Assert.That(outer.OwnMilliseconds).IsEqualTo(200);
	}
}
