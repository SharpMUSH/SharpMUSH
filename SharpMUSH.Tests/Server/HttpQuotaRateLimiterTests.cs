using SharpMUSH.Server.RateLimiting;
using System.Threading.RateLimiting;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// Issue #1121: the <c>http_per_second</c> admission control behind the <c>/http/{**path}</c>
/// route, checked against PennMUSH's own arithmetic (src/bsd.c:1008-1023) on a clock the test
/// owns, so nothing here depends on how long the test itself takes to run.
/// </summary>
public class HttpQuotaRateLimiterTests
{
	private sealed class Clock : TimeProvider
	{
		private long _ticks;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp() => _ticks;

		public void Advance(TimeSpan by) => _ticks += by.Ticks;
	}

	private static int Drain(RateLimiter limiter, int attempts)
	{
		var acquired = 0;
		for (var i = 0; i < attempts; i++)
		{
			using var lease = limiter.AttemptAcquire();
			if (lease.IsAcquired) acquired++;
		}

		return acquired;
	}

	[Test]
	public async Task TheBucketStartsFullAndHoldsExactlyTheConfiguredBurst()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(3, clock);

		// No time passes across these four attempts, so refill cannot cover the fourth.
		await Assert.That(Drain(limiter, 4)).IsEqualTo(3);
	}

	[Test]
	public async Task RefillIsContinuous_NotPerWindow()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(3, clock);
		await Assert.That(Drain(limiter, 3)).IsEqualTo(3);

		// Penn credits http_per_second per elapsed millisecond and spends 1000 per request, so a
		// third of a second buys back exactly one permit — 333ms is 999 quota and buys none.
		clock.Advance(TimeSpan.FromMilliseconds(333));
		await Assert.That(Drain(limiter, 1)).IsEqualTo(0);

		clock.Advance(TimeSpan.FromMilliseconds(1));
		await Assert.That(Drain(limiter, 2)).IsEqualTo(1);
	}

	[Test]
	public async Task IdleTimeDoesNotAccumulateBeyondTheBurstCeiling()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(4, clock);
		await Assert.That(Drain(limiter, 4)).IsEqualTo(4);

		// bsd.c:1009-1011 clamps the quota at http_per_second * MS_PER_SEC, so ten quiet seconds
		// are worth one burst, never forty requests.
		clock.Advance(TimeSpan.FromSeconds(10));
		await Assert.That(Drain(limiter, 40)).IsEqualTo(4);
	}

	[Test]
	public async Task ARefusedLeaseSaysWhenToComeBack()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(2, clock);
		Drain(limiter, 2);

		using var refused = limiter.AttemptAcquire();

		await Assert.That(refused.IsAcquired).IsFalse();
		await Assert.That(refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)).IsTrue();
		await Assert.That(retryAfter).IsGreaterThan(TimeSpan.Zero);
	}

	[Test]
	public async Task StatisticsReportThePermitsLeftAndTheDecisionsMade()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(3, clock);
		Drain(limiter, 5);

		var statistics = limiter.GetStatistics()!;

		await Assert.That(statistics.CurrentAvailablePermits).IsEqualTo(0);
		await Assert.That(statistics.TotalSuccessfulLeases).IsEqualTo(3);
		await Assert.That(statistics.TotalFailedLeases).IsEqualTo(2);
	}

	[Test]
	public async Task ADisabledQuotaIsNeverExpressedAsALimiter()
	{
		// http_per_second below one means "HTTP is off" (bsd.c:3741), which the dispatcher answers
		// as an unconfigured handler — constructing a zero-rate bucket would instead wedge the
		// route at 429 forever, so the type refuses to be built that way.
		await Assert.That(() => new HttpQuotaRateLimiter(0)).Throws<ArgumentOutOfRangeException>();
	}
}
