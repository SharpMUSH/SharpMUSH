using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class QueueProfileRecorderTests
{
	private static CapabilityActor Actor(string account = "account") => new(account, new DBRef(1, 100), new DBRef(1, 100));

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ReplacementImmediatelyFreesOnlyTheRetiredGeneration(bool anotherProfile)
	{
		var recorder = new QueueDiagnosticsRecorder();
		var old = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
		var other = anotherProfile ? recorder.StartProfile(Actor("other"), TimeSpan.FromMinutes(1)) : null;
		var count = QueueDiagnosticsRecorder.ProfileMailboxCapacity / (anotherProfile ? 2 : 1);
		for (var i = 0; i < count; i++) recorder.RecordInvocation(new(TelemetryInvocationKind.Function, $"F{i}", 1, true));
		var current = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
		recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "NEW", 1, true));
		var samples = recorder.DrainProfileSamples();
		await Assert.That(samples.Count(s => s.ProfileId == current.Id)).IsEqualTo(1);
		await Assert.That(samples.Any(s => s.ProfileId == old.Id)).IsFalse();
		await Assert.That(recorder.Profile(current.Id)!.DroppedSamples).IsEqualTo(0L);
		if (other is not null)
		{
			await Assert.That(samples.Where(s => s.ProfileId == other.Id).Select(s => s.Invocation.Name).SequenceEqual(
				Enumerable.Range(0, count).Select(i => $"F{i}").Append("NEW"))).IsTrue();
			await Assert.That(recorder.Profile(other.Id)!.DroppedSamples).IsEqualTo(0L);
		}
	}

	[Test]
	public async Task ConcurrentWritersCannotRetainARetiredGeneration()
	{
		var recorder = new QueueDiagnosticsRecorder();
		for (var attempt = 0; attempt < 32; attempt++)
		{
			var old = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
			recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "BEFORE", 1, true));
			using var start = new Barrier(2);
			var writer = Task.Run(() =>
			{
				start.SignalAndWait();
				for (var i = 0; i < 256; i++) recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "RACE", 1, true));
			});
			start.SignalAndWait();
			var current = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
			await writer;
			var samples = recorder.DrainProfileSamples();
			await Assert.That(samples.All(s => s.ProfileId == current.Id)).IsTrue();
			await Assert.That(recorder.Profile(old.Id)).IsNull();
		}
	}

	[Test]
	public async Task MailboxAndAggregateCardinalityStayBounded()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var profile = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
		for (var i = 0; i < QueueDiagnosticsRecorder.ProfileMailboxCapacity + 7; i++)
			recorder.RecordInvocation(new(TelemetryInvocationKind.Function, $"FUNCTION{i}", 1, true));
		var samples = recorder.DrainProfileSamples();
		await Assert.That(samples.Count).IsEqualTo(QueueDiagnosticsRecorder.ProfileMailboxCapacity);
		recorder.ApplyProfileSamples(profile.Id, samples);
		var result = recorder.Profile(profile.Id)!;
		await Assert.That(result.Aggregates.Count).IsEqualTo(QueueDiagnosticsRecorder.ProfileKeyCapacity);
		await Assert.That(result.DroppedSamples).IsEqualTo(7L + QueueDiagnosticsRecorder.ProfileMailboxCapacity - QueueDiagnosticsRecorder.ProfileKeyCapacity);
	}

	[Test]
	public async Task ReplacementDiscardsOldGenerationSamplesAndOwnerIsIsolated()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var old = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
		recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "old", 1, true));
		var current = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
		recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "new", 2, false));
		var samples = recorder.DrainProfileSamples();
		recorder.ApplyProfileSamples(old.Id, samples);
		recorder.ApplyProfileSamples(current.Id, samples);
		await Assert.That(recorder.Profile(old.Id)).IsNull();
		var row = recorder.Profile(current.Id)!.Aggregates.Single();
		await Assert.That(row.Name).IsEqualTo("NEW");
		await Assert.That(row.Count).IsEqualTo(1L);
		await Assert.That(row.Failures).IsEqualTo(1L);
	}

	[Test]
	public async Task ExpiryStopsCaptureAndRetentionDoesNotDependOnPollingFrequency()
	{
		var clock = new QueueDiagnosticsRecorderTests.Clock();
		var recorder = new QueueDiagnosticsRecorder(clock);
		var profile = recorder.StartProfile(Actor(), TimeSpan.FromSeconds(2))!;
		clock.Advance(TimeSpan.FromSeconds(3));
		recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "late", 1, true));
		await Assert.That(recorder.DrainProfileSamples().Count).IsEqualTo(0);
		await Assert.That(recorder.Profile(profile.Id)!.Recording).IsFalse();
		clock.Advance(QueueDiagnosticsRecorder.Retention);
		await Assert.That(recorder.Profile(profile.Id)).IsNull();
		var second = recorder.StartProfile(Actor(), TimeSpan.FromSeconds(1))!;
		clock.Advance(TimeSpan.FromHours(1));
		await Assert.That(recorder.Profile(second.Id)).IsNull();
	}

	[Test]
	public async Task ActiveSessionsAreBoundedAndStoppedSlotsCanBeReused()
	{
		var recorder = new QueueDiagnosticsRecorder();
		for (var i = 0; i < QueueDiagnosticsRecorder.ProfileCapacity; i++)
			await Assert.That(recorder.StartProfile(Actor($"account{i}"), TimeSpan.FromMinutes(1))).IsNotNull();
		await Assert.That(recorder.StartProfile(Actor("overflow"), TimeSpan.FromMinutes(1))).IsNull();
		recorder.StopProfile(recorder.ProfileRegistrations().First().Id);
		await Assert.That(recorder.StartProfile(Actor("replacement"), TimeSpan.FromMinutes(1))).IsNotNull();
		await Assert.That(recorder.ProfileRegistrations().Count).IsEqualTo(QueueDiagnosticsRecorder.ProfileCapacity);
	}

	[Test]
	public async Task DiscardedProfileCannotRetainQueuedSamples()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var profile = recorder.StartProfile(Actor(), TimeSpan.FromMinutes(1))!;
		recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "private", 1, true));
		recorder.StopProfile(profile.Id, discard: true);
		var samples = recorder.DrainProfileSamples();
		await Assert.That(samples.Count).IsEqualTo(0);
		recorder.ApplyProfileSamples(profile.Id, samples);
		await Assert.That(recorder.Profile(profile.Id)).IsNull();
	}
}
