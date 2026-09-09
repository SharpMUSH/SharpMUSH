using System.Threading.Channels;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public sealed record QueueProfileRegistration(Guid Id, CapabilityActor Actor, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt);
public readonly record struct QueueProfileSample(Guid ProfileId, DBRef? Source, DBRef? Owner,
	string? SourceAttribute, TelemetryInvocation Invocation);
public sealed record QueueProfileAggregate(DBRef? Source, DBRef? Owner, string? SourceAttribute,
	TelemetryInvocationKind Kind, string Name, long Count, long Failures, double InclusiveMilliseconds, double MaximumMilliseconds);
public sealed record QueueProfileSnapshot(QueueProfileRegistration Registration, bool Recording,
	long DroppedSamples, IReadOnlyList<QueueProfileAggregate> Aggregates);

public sealed partial class QueueDiagnosticsRecorder
{
	public const int ProfileCapacity = 8;
	public const int ProfileKeyCapacity = 256;
	public const int ProfileMailboxCapacity = 4096;
	private readonly object _profileGate = new();
	private readonly Dictionary<Guid, ProfileCapture> _profiles = new();
	private ProfileCapture[] _recording = [];
	private readonly Channel<QueueProfileSample> _samples = Channel.CreateBounded<QueueProfileSample>(
		new BoundedChannelOptions(ProfileMailboxCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
	private readonly record struct AggregateKey(DBRef? Source, DBRef? Owner, string? Attribute, TelemetryInvocationKind Kind, string Name);
	private sealed class ProfileCapture(QueueProfileRegistration registration, long startStamp, TimeSpan duration)
	{
		public QueueProfileRegistration Registration { get; } = registration;
		public long StartStamp { get; } = startStamp;
		public TimeSpan Duration { get; } = duration;
		public long? StopStamp;
		public int Recording = 1;
		public long Dropped;
		public readonly Dictionary<AggregateKey, QueueProfileAggregate> Aggregates = new();
	}

	/// <summary>Called only after the shared facade validates current profiling and inspection authority.</summary>
	public QueueProfileRegistration? StartProfile(CapabilityActor actor, TimeSpan duration)
	{
		if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromSeconds(300)) return null;
		lock (_profileGate)
		{
			PruneProfiles();
			foreach (var prior in _profiles.Values.Where(p => p.Registration.Actor.AccountId == actor.AccountId).ToArray())
			{
				Volatile.Write(ref prior.Recording, 0);
				_profiles.Remove(prior.Registration.Id);
			}
			if (_profiles.Count >= ProfileCapacity)
			{
				var stopped = _profiles.Values.Where(p => p.StopStamp is not null).MinBy(p => p.StopStamp);
				if (stopped is null) return null;
				_profiles.Remove(stopped.Registration.Id);
			}
			var now = _clock.GetUtcNow();
			var registration = new QueueProfileRegistration(Guid.NewGuid(), actor, now, now + duration);
			_profiles.Add(registration.Id, new(registration, _clock.GetTimestamp(), duration));
			RefreshRecording();
			return registration;
		}
	}
	private void RefreshRecording() => Volatile.Write(ref _recording, _profiles.Values.Where(p => p.Recording == 1).ToArray());
	private void PruneProfiles()
	{
		var now = _clock.GetTimestamp();
		foreach (var capture in _profiles.Values.ToArray())
		{
			if (capture.Recording == 1 && _clock.GetElapsedTime(capture.StartStamp, now) >= capture.Duration)
			{
				Volatile.Write(ref capture.Recording, 0);
				capture.StopStamp = capture.StartStamp + (long)(capture.Duration.TotalSeconds * _clock.TimestampFrequency);
			}
			if (capture.StopStamp is { } stopped && _clock.GetElapsedTime(stopped, now) >= Retention)
				_profiles.Remove(capture.Registration.Id);
		}
		RefreshRecording();
	}
	public IReadOnlyList<QueueProfileRegistration> ProfileRegistrations()
	{
		lock (_profileGate) { PruneProfiles(); return _profiles.Values.Select(p => p.Registration).ToArray(); }
	}
	public void StopProfile(Guid id, bool discard = false)
	{
		lock (_profileGate)
		{
			if (!_profiles.TryGetValue(id, out var capture)) return;
			Volatile.Write(ref capture.Recording, 0);
			capture.StopStamp ??= _clock.GetTimestamp();
			if (discard) _profiles.Remove(id);
			RefreshRecording();
		}
	}
	public bool IsProfileRecording(Guid id)
	{
		lock (_profileGate) return _profiles.TryGetValue(id, out var capture) && capture.Recording == 1;
	}
	public QueueProfileSnapshot? Profile(Guid id)
	{
		lock (_profileGate)
		{
			PruneProfiles();
			return _profiles.TryGetValue(id, out var capture)
				? new(capture.Registration, capture.Recording == 1, Interlocked.Read(ref capture.Dropped), capture.Aggregates.Values.ToArray())
				: null;
		}
	}
	partial void RecordProfileSample(QueueObservation? observation, TelemetryInvocation invocation)
	{
		var captures = Volatile.Read(ref _recording);
		if (captures.Length == 0 || invocation.Kind is not (TelemetryInvocationKind.Function or TelemetryInvocationKind.Command)
			|| !double.IsFinite(invocation.ElapsedMilliseconds) || invocation.ElapsedMilliseconds < 0) return;
		var name = invocation.Name;
		if (name is null || name.Length is 0 or > 128 || name.Any(char.IsControl)) name = "(other)";
		name = name.ToUpperInvariant();
		invocation = invocation with { Name = name.Length <= 128 ? name : "(other)" };
		var now = _clock.GetTimestamp();
		foreach (var capture in captures)
		{
			if (Volatile.Read(ref capture.Recording) == 0 || _clock.GetElapsedTime(capture.StartStamp, now) >= capture.Duration) continue;
			if (!_samples.Writer.TryWrite(new(capture.Registration.Id, observation?.Source, observation?.Owner,
				observation?.SourceAttribute, invocation))) Interlocked.Increment(ref capture.Dropped);
		}
	}
	public IReadOnlyList<QueueProfileSample> DrainProfileSamples()
	{
		var result = new List<QueueProfileSample>();
		while (result.Count < ProfileMailboxCapacity && _samples.Reader.TryRead(out var sample)) result.Add(sample);
		return result;
	}
	/// <summary>Accepts a bounded batch only after fresh capability and resource inspection checks.</summary>
	public void ApplyProfileSamples(Guid id, IReadOnlyList<QueueProfileSample> samples)
	{
		lock (_profileGate)
		{
			PruneProfiles();
			if (!_profiles.TryGetValue(id, out var capture)) return;
			foreach (var sample in samples.Take(ProfileMailboxCapacity))
			{
				if (sample.ProfileId != id) continue;
				var invocation = sample.Invocation;
				var key = new AggregateKey(sample.Source, sample.Owner, sample.SourceAttribute, invocation.Kind, invocation.Name);
				if (!capture.Aggregates.TryGetValue(key, out var previous))
				{
					if (capture.Aggregates.Count >= ProfileKeyCapacity) { Interlocked.Increment(ref capture.Dropped); continue; }
					previous = new(sample.Source, sample.Owner, sample.SourceAttribute, invocation.Kind, invocation.Name, 0, 0, 0, 0);
				}
				capture.Aggregates[key] = previous with
				{
					Count = previous.Count + 1,
					Failures = previous.Failures + (invocation.Success ? 0 : 1),
					InclusiveMilliseconds = Math.Min(double.MaxValue, previous.InclusiveMilliseconds + invocation.ElapsedMilliseconds),
					MaximumMilliseconds = Math.Max(previous.MaximumMilliseconds, invocation.ElapsedMilliseconds)
				};
			}
		}
	}
}
