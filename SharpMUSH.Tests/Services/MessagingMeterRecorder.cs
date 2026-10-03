using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

/// <summary>Records the counters of one <see cref="NatsMessagingMetrics"/> instance, ignoring every other host's.</summary>
internal sealed class MessagingMeterRecorder : IDisposable
{
	private readonly MeterListener _listener = new();
	private readonly ConcurrentQueue<(string Name, long Value, Dictionary<string, object?> Tags)> _measurements = new();

	public MessagingMeterRecorder(NatsMessagingMetrics metrics)
	{
		_listener.InstrumentPublished = (instrument, owner) =>
		{
			if (ReferenceEquals(instrument.Meter, metrics.Meter)) owner.EnableMeasurementEvents(instrument);
		};
		_listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
		{
			var map = new Dictionary<string, object?>();
			foreach (var tag in tags) map[tag.Key] = tag.Value;
			_measurements.Enqueue((instrument.Name, value, map));
		});
		_listener.Start();
	}

	public long Sum(string name, string? tagKey = null, object? tagValue = null) => _measurements
		.Where(m => m.Name == name && (tagKey is null || Equals(m.Tags.GetValueOrDefault(tagKey), tagValue)))
		.Sum(m => m.Value);

	public void Dispose() => _listener.Dispose();
}
