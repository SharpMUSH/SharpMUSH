using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Reads the bus streams, this process's consumers and the account's JetStream storage on an interval
/// and hands the figures to <see cref="NatsMessagingMetrics"/>. Warns when a stream or the storage
/// budget is four-fifths full, which is when a discard-new stream is about to start refusing work.
/// </summary>
public sealed class NatsBrokerMonitor(
	NatsOptions options,
	NatsMessagingMetrics metrics,
	ILogger<NatsBrokerMonitor> logger,
	NatsConsumerRegistry? registry = null) : BackgroundService
{
	private const double WarnFraction = 0.8;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await using var nats = new NatsConnection(new NatsOpts { Url = options.Url });
				await nats.ConnectAsync();
				var js = new NatsJSContext(nats);
				using var timer = new PeriodicTimer(options.MonitorInterval);
				do
				{
					var snapshot = await ReadAsync(js, stoppingToken);
					metrics.Record(snapshot);
					WarnWhenNearlyFull(snapshot);
				} while (await timer.WaitForNextTickAsync(stoppingToken));
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				// Monitoring must never take the host down: a broker outage is reported by readiness, and
				// the next reading resumes once it is back.
				logger.LogWarning(ex, "[NATS-MONITOR] Could not read broker figures; retrying after {Interval}", options.MonitorInterval);
				try { await Task.Delay(options.MonitorInterval, stoppingToken); }
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
			}
		}
	}

	/// <summary>Streams this process watches: the one it publishes to, the one it consumes, and any extras.</summary>
	internal IEnumerable<string> StreamNames() =>
		new[] { options.StreamName, options.GetConsumeStreamName() }.Concat(options.MonitoredStreams).Distinct();

	internal async Task<NatsBrokerSnapshot> ReadAsync(INatsJSContext js, CancellationToken ct)
	{
		var streams = new List<NatsStreamReading>();
		foreach (var name in StreamNames())
		{
			try
			{
				var stream = await js.GetStreamAsync(name, cancellationToken: ct);
				streams.Add(new NatsStreamReading(name, (long)stream.Info.State.Bytes, (long)stream.Info.State.Messages,
					stream.Info.Config.MaxBytes));
			}
			catch (NatsJSApiException ex) when (ex.Error.ErrCode == NatsStreamPolicy.StreamNotFound)
			{
				logger.LogDebug("[NATS-MONITOR] Stream {Stream} does not exist yet", name);
			}
		}

		var consumers = new List<NatsConsumerReading>();
		foreach (var registration in registry?.Registrations ?? [])
		{
			var stream = options.GetConsumeStreamName();
			try
			{
				var consumer = await js.GetConsumerAsync(stream, registration.DurableName, ct);
				consumers.Add(new NatsConsumerReading(stream, registration.DurableName, (long)consumer.Info.NumPending,
					consumer.Info.NumAckPending, consumer.Info.NumRedelivered));
			}
			catch (NatsJSApiException ex) when (ex.Error.Code == 404)
			{
				logger.LogDebug("[NATS-MONITOR] Consumer {Durable} does not exist yet", registration.DurableName);
			}
		}

		var account = await js.GetAccountInfoAsync(ct);
		return new NatsBrokerSnapshot(streams, consumers, new NatsStorageReading((long)account.Storage, account.Limits.MaxStorage));
	}

	private void WarnWhenNearlyFull(NatsBrokerSnapshot snapshot)
	{
		foreach (var stream in snapshot.Streams.Where(stream => stream.MaxBytes > 0 && stream.Bytes >= stream.MaxBytes * WarnFraction))
			logger.LogWarning("[NATS-MONITOR] Stream {Stream} holds {Bytes} of its {MaxBytes}-byte budget",
				stream.Name, stream.Bytes, stream.MaxBytes);
		if (snapshot.Storage is { Limit: > 0 } storage && storage.Used >= storage.Limit * WarnFraction)
			logger.LogWarning("[NATS-MONITOR] JetStream uses {Used} of its {Limit}-byte storage budget", storage.Used, storage.Limit);
	}
}
