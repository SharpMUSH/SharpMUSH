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

		var outer = InvocationClock.Start(null);
		time.Advance(1);
		var inner = InvocationClock.Start(outer);
		time.Advance(200);
		inner.Stop();
		time.Advance(2);
		outer.Stop();

		await Assert.That(inner.OwnMilliseconds).IsEqualTo(200);
		await Assert.That(outer.OwnMilliseconds).IsEqualTo(3);
		await Assert.That(outer.InclusiveMilliseconds).IsEqualTo(203);
	}

	[Test]
	public async Task ACallOnAFreshParserPausesTheCallRunningInItsFlow()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var outer = InvocationClock.Start(null);
		time.Advance(1);
		var lockAttribute = InvocationClock.Start(null);
		time.Advance(200);
		lockAttribute.Stop();
		time.Advance(2);
		outer.Stop();

		await Assert.That(lockAttribute.OwnMilliseconds).IsEqualTo(200);
		await Assert.That(outer.OwnMilliseconds).IsEqualTo(3);
	}

	[Test]
	public async Task ACallerThatHasStoppedLeavesTheCallToTheFlow()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var queued = InvocationClock.Start(null);
		queued.Stop();
		var running = InvocationClock.Start(null);
		var nested = InvocationClock.Start(queued);
		time.Advance(200);
		nested.Stop();
		time.Advance(1);
		running.Stop();

		await Assert.That(running.OwnMilliseconds).IsEqualTo(1);
	}

	[Test]
	public async Task EvaluatingArgumentsPausesTheCall()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var clock = InvocationClock.Start(null);
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

		var command = InvocationClock.Start(null);
		using (InvocationClock.PauseRunning(command))
			time.Advance(200);
		using (InvocationClock.PauseRunning(null))
			time.Advance(200);
		time.Advance(1);
		command.Stop();

		await Assert.That(command.OwnMilliseconds).IsEqualTo(1);
		using (InvocationClock.PauseRunning(command)) { }
	}

	[Test]
	public async Task AStoppedCallLeavesWhatFollowsToItsCaller()
	{
		var time = new ManualTime();
		using var _ = InvocationClock.UseTime(time);

		var outer = InvocationClock.Start(null);
		var prose = InvocationClock.Start(outer);
		prose.Stop();
		var after = InvocationClock.Start(null);
		time.Advance(200);
		after.Stop();
		time.Advance(200);
		prose.Stop();
		outer.Stop();

		await Assert.That(prose.OwnMilliseconds).IsEqualTo(0);
		await Assert.That(outer.OwnMilliseconds).IsEqualTo(200);
	}
}
