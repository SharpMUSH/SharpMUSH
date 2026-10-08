using SharpMUSH.Implementation.Visitors;

namespace SharpMUSH.Tests.Services;

/// <summary>A call's clock leaves out its arguments and the calls nested inside it.</summary>
public class InvocationClockTests
{
	private static readonly TimeSpan Work = TimeSpan.FromMilliseconds(200);

	[Test]
	public async Task ANestedCallPausesItsCaller()
	{
		var outer = InvocationClock.Start();
		var inner = InvocationClock.Start();
		await Task.Delay(Work);
		inner.Stop();
		outer.Stop();

		await Assert.That(inner.OwnMilliseconds).IsGreaterThanOrEqualTo(Work.TotalMilliseconds * 0.9);
		await Assert.That(outer.InclusiveMilliseconds).IsGreaterThanOrEqualTo(Work.TotalMilliseconds * 0.9);
		await Assert.That(outer.OwnMilliseconds).IsLessThan(Work.TotalMilliseconds / 2);
	}

	[Test]
	public async Task EvaluatingArgumentsPausesTheCall()
	{
		var clock = InvocationClock.Start();
		using (clock.Pause())
		{
			using (clock.Pause())
				await Task.Delay(Work);
			await Task.Delay(Work);
		}

		clock.Stop();

		await Assert.That(clock.InclusiveMilliseconds).IsGreaterThanOrEqualTo(Work.TotalMilliseconds * 1.8);
		await Assert.That(clock.OwnMilliseconds).IsLessThan(Work.TotalMilliseconds / 2);
	}

	[Test]
	public async Task AStoppedCallLeavesWhatFollowsToItsCaller()
	{
		var outer = InvocationClock.Start();
		var prose = InvocationClock.Start();
		prose.Stop();
		await Task.Delay(Work);
		prose.Stop();
		outer.Stop();

		await Assert.That(prose.OwnMilliseconds).IsLessThan(Work.TotalMilliseconds / 2);
		await Assert.That(outer.OwnMilliseconds).IsGreaterThanOrEqualTo(Work.TotalMilliseconds * 0.9);
	}
}
