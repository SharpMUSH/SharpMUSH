using System.Threading.RateLimiting;

namespace SharpMUSH.Server.RateLimiting;

/// <summary>
/// PennMUSH's <c>http_per_second</c> quota (src/bsd.c, <c>http_quota</c>) expressed as a
/// <see cref="RateLimiter"/>, so the softcode HTTP surface at <c>/http/{**path}</c> admits traffic
/// on the same terms Penn does.
/// <para>
/// Penn counts in milliseconds rather than requests: one handled request costs
/// <see cref="MillisecondsPerSecond"/>, every elapsed millisecond credits <c>http_per_second</c>,
/// and the balance is capped at <c>http_per_second * 1000</c> (bsd.c:1008-1011). That is a token
/// bucket refilling continuously at <c>http_per_second</c> permits a second whose burst ceiling is
/// <c>http_per_second</c> permits. Penn holds it in one process-wide counter — one game, one
/// budget, not a per-client allowance — so callers partition this limiter globally.
/// </para>
/// <para>
/// Two deliberate departures from Penn. It starts at the ceiling rather than empty: Penn's timer
/// fills the counter during the first second of uptime, and an ASP.NET host has no equivalent
/// warm-up tick, so a cold bucket would refuse the first request after every restart. And an
/// over-quota request is refused rather than deferred: Penn parks the descriptor and retries it on
/// a later pass (bsd.c:3637-3648), which a live HTTP request has no equivalent of, so the caller
/// answers 429 carrying the <c>Retry-After</c> that <c>http_msecs_till_next</c> computes.
/// </para>
/// </summary>
public sealed class HttpQuotaRateLimiter : RateLimiter
{
	/// <summary>Penn's <c>MS_PER_SEC</c>: the quota one handled request spends.</summary>
	private const long MillisecondsPerSecond = 1000;

	private readonly long _perSecond;
	private readonly long _ceiling;
	private readonly TimeProvider _time;
	private readonly Lock _gate = new();

	private long _quota;
	private long _lastTimestamp;
	private long _lastFullTimestamp;
	private long _successfulLeases;
	private long _failedLeases;

	/// <param name="permitsPerSecond">The configured <c>http_per_second</c>; must be positive.</param>
	/// <param name="timeProvider">Clock driving the refill; tests supply a controllable one.</param>
	public HttpQuotaRateLimiter(uint permitsPerSecond, TimeProvider? timeProvider = null)
	{
		ArgumentOutOfRangeException.ThrowIfZero(permitsPerSecond);

		_perSecond = permitsPerSecond;
		_ceiling = _perSecond * MillisecondsPerSecond;
		_time = timeProvider ?? TimeProvider.System;
		_lastTimestamp = _time.GetTimestamp();
		_lastFullTimestamp = _lastTimestamp;
		_quota = _ceiling;
	}

	/// <summary>How long the bucket has sat at its ceiling — the middleware's cue to evict it.</summary>
	public override TimeSpan? IdleDuration
	{
		get
		{
			lock (_gate)
			{
				Refill();
				return _quota >= _ceiling ? _time.GetElapsedTime(_lastFullTimestamp) : null;
			}
		}
	}

	public override RateLimiterStatistics GetStatistics()
	{
		lock (_gate)
		{
			Refill();
			return new RateLimiterStatistics
			{
				CurrentAvailablePermits = _quota / MillisecondsPerSecond,
				CurrentQueuedCount = 0,
				TotalSuccessfulLeases = _successfulLeases,
				TotalFailedLeases = _failedLeases
			};
		}
	}

	protected override RateLimitLease AttemptAcquireCore(int permitCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(permitCount);

		lock (_gate)
		{
			Refill();

			var cost = permitCount * MillisecondsPerSecond;
			if (_quota < cost)
			{
				_failedLeases++;
				return new Lease(false, RetryAfter(cost));
			}

			_quota -= cost;
			_successfulLeases++;
			return new Lease(true, null);
		}
	}

	/// <summary>Nothing queues here — Penn's quota has no waiting room — so this is the sync path.</summary>
	protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(AttemptAcquireCore(permitCount));
	}

	/// <summary>
	/// bsd.c:1008-1011 — credit the elapsed milliseconds, then clamp to the ceiling. A call that
	/// lands inside the same millisecond leaves the clock alone so the remainder is not discarded.
	/// </summary>
	private void Refill()
	{
		var now = _time.GetTimestamp();
		var milliseconds = (long)_time.GetElapsedTime(_lastTimestamp, now).TotalMilliseconds;
		if (milliseconds <= 0)
		{
			return;
		}

		_lastTimestamp = now;
		if (_quota >= _ceiling)
		{
			// Already full: leave _lastFullTimestamp on the moment it BECAME full, or an idle
			// bucket would report no idle time at all and never be evicted.
			return;
		}

		_quota = Math.Min(_ceiling, _quota + (milliseconds * _perSecond));
		if (_quota >= _ceiling)
		{
			_lastFullTimestamp = now;
		}
	}

	/// <summary>
	/// When the refused request could be afforded, from the quota it is actually short of.
	/// <para>
	/// Penn's <c>http_msecs_till_next</c> (bsd.c:1017-1023) is deliberately not copied here: it
	/// feeds Penn's <c>select()</c> poll ceiling (bsd.c <c>min_timeout</c>), not a promise made to
	/// a client, and its trailing <c>+ HTTP_SECOND_LIMIT</c> term scales with the configured rate
	/// rather than with the shortfall — at a limit of 100000 it would tell a caller whose wait is
	/// measured in microseconds to come back in 100 seconds.
	/// </para>
	/// </summary>
	private TimeSpan RetryAfter(long cost)
	{
		var deficit = cost - _quota;
		return deficit <= 0
			? TimeSpan.Zero
			: TimeSpan.FromMilliseconds(Math.Ceiling((double)deficit / _perSecond));
	}

	private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
	{
		public override bool IsAcquired => acquired;

		public override IEnumerable<string> MetadataNames =>
			retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

		public override bool TryGetMetadata(string metadataName, out object? metadata)
		{
			if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
			{
				metadata = value;
				return true;
			}

			metadata = null;
			return false;
		}
	}
}
