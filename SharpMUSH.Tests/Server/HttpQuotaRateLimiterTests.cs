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
		// An empty bucket at 2/second is one permit away in 500ms, and the wait is measured from
		// that shortfall — not from Penn's http_msecs_till_next, whose trailing term scales with
		// the configured rate and would answer "2.5 seconds" here and 100 seconds at the maximum.
		await Assert.That(retryAfter).IsEqualTo(TimeSpan.FromMilliseconds(500));
	}

	[Test]
	public async Task TheWaitShrinksAsTheBucketRefills()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(4, clock);
		Drain(limiter, 4);

		clock.Advance(TimeSpan.FromMilliseconds(200));

		using var refused = limiter.AttemptAcquire();
		refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);

		// 200ms bought 800 of the 1000 quota a permit costs; the remaining 200 is 50ms at 4/second.
		await Assert.That(retryAfter).IsEqualTo(TimeSpan.FromMilliseconds(50));
	}

	[Test]
	public async Task AFullBucketReportsHowLongItHasBeenIdle()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(3, clock);

		clock.Advance(TimeSpan.FromMinutes(2));

		// The middleware evicts a partition that has been idle long enough. Re-stamping the "full
		// since" mark on every refill would peg this at zero and keep every bucket alive forever —
		// including the one left behind by each superseded http_per_second setting.
		await Assert.That(limiter.IdleDuration).IsEqualTo(TimeSpan.FromMinutes(2));
	}

	[Test]
	public async Task ABucketWithPermitsOutstandingIsNotIdle()
	{
		var clock = new Clock();
		using var limiter = new HttpQuotaRateLimiter(3, clock);
		Drain(limiter, 1);

		clock.Advance(TimeSpan.FromMilliseconds(100));
		await Assert.That(limiter.IdleDuration).IsNull();

		// Once it refills to the ceiling the idle clock starts again from that moment, not from
		// construction.
		clock.Advance(TimeSpan.FromSeconds(1));
		await Assert.That(limiter.IdleDuration).IsEqualTo(TimeSpan.Zero);

		clock.Advance(TimeSpan.FromSeconds(5));
		await Assert.That(limiter.IdleDuration).IsEqualTo(TimeSpan.FromSeconds(5));
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
