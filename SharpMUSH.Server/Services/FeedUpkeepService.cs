using System.Diagnostics.Metrics;
using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Keeps feeds inside their <c>max_age</c> and reports what they hold.
/// <list type="bullet">
///   <item>Every hour, drops the lines older than their feed's <c>max_age</c>
///   (<see cref="IFeedService.PurgeExpiredAsync"/>). A feed also ages each time it is written to; this is for
///   the feeds nobody writes to any more.</item>
///   <item><c>sharpmush_feed_lines{kind=…}</c> and <c>sharpmush_feed_stored_bytes{kind=…}</c>: each kind's
///   lines and what they take in the store, so an alert can fire on a kind that grows without bound.</item>
/// </list>
/// </summary>
public sealed class FeedUpkeepService : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

	/// <summary>A scrape reads both gauges in a row; one read of the feed rows serves them.</summary>
	private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(15);

	private readonly IFeedService _feeds;
	private readonly IMediator _mediator;
	private readonly TimeProvider _time;
	private readonly ILogger<FeedUpkeepService> _logger;
	private readonly Meter _meter;
	private readonly Lock _gate = new();
	private IReadOnlyList<SharpFeedUsage> _last = [];
	private DateTimeOffset _measuredAt = DateTimeOffset.MinValue;

	public FeedUpkeepService(IFeedService feeds, IMediator mediator, ILogger<FeedUpkeepService> logger)
		: this(feeds, mediator, TimeProvider.System, logger)
	{
	}

	public FeedUpkeepService(IFeedService feeds, IMediator mediator, TimeProvider time, ILogger<FeedUpkeepService> logger)
	{
		_feeds = feeds;
		_mediator = mediator;
		_time = time;
		_logger = logger;
		// The same meter name the host's OpenTelemetry pipeline already exports.
		_meter = new Meter("SharpMUSH", "1.0.0");
		_meter.CreateObservableGauge("sharpmush.feed.lines",
			() => Current().Select(usage => Measurement(usage.Messages, usage.Kind)),
			description: "Lines held by each feed kind's feeds");
		_meter.CreateObservableGauge("sharpmush.feed.stored_bytes",
			() => Current().Select(usage => Measurement(usage.StoredBytes, usage.Kind)), unit: "By",
			description: "What each feed kind's lines take in the store, names and keys included");
	}

	private static Measurement<long> Measurement(long value, string kind)
		=> new(value, new KeyValuePair<string, object?>("kind", kind));

	private IReadOnlyList<SharpFeedUsage> Current()
	{
		lock (_gate)
		{
			var now = _time.GetUtcNow();
			if (now - _measuredAt > Freshness)
			{
				// The provider reads synchronously; the query completes before it returns.
				_last = _mediator.Send(new GetFeedUsageQuery()).AsTask().GetAwaiter().GetResult();
				_measuredAt = now;
			}

			return _last;
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval, _time);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					var purged = await _feeds.PurgeExpiredAsync(_time.GetUtcNow(), stoppingToken);
					if (purged > 0) _logger.LogInformation("Dropped {Lines} feed line(s) past their feed's max_age", purged);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// The next tick tries again; a line past its age is still dropped on the feed's next write.
					_logger.LogError(ex, "Feed max_age upkeep failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug("Feed upkeep stopped for shutdown");
		}
	}

	public override void Dispose()
	{
		_meter.Dispose();
		base.Dispose();
	}
}
