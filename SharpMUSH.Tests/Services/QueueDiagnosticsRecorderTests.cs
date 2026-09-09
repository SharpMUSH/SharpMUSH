using System.Text.Json;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class QueueDiagnosticsRecorderTests
{
	internal sealed class Clock : TimeProvider
	{
		public DateTimeOffset Utc = DateTimeOffset.UnixEpoch;
		public long Stamp;
		public override DateTimeOffset GetUtcNow() => Utc;
		public override long GetTimestamp() => Stamp;
		public override long TimestampFrequency => TimeSpan.TicksPerSecond;
		public void Advance(TimeSpan elapsed) { Stamp += elapsed.Ticks; Utc += elapsed; }
	}

	[Test]
	public async Task HistoryIsBoundedAndExpiresUnderSustainedRejections()
	{
		var clock = new Clock(); var recorder = new QueueDiagnosticsRecorder(clock);
		for (var i = 0; i < QueueDiagnosticsRecorder.HistoryCapacity + 20; i++)
			recorder.Rejected(new DBRef(1, 100), new DBRef(2, 100), "enqueue", QueueOutcome.OwnerLimit);
		var rows = recorder.Recent();
		await Assert.That(rows.Count).IsEqualTo(QueueDiagnosticsRecorder.HistoryCapacity);
		await Assert.That(rows.Last().Sequence).IsEqualTo(21L);
		await Assert.That(rows.All(row => row.Pid is null && row.Outcome == QueueOutcome.OwnerLimit)).IsTrue();
		clock.Advance(QueueDiagnosticsRecorder.Retention);
		await Assert.That(recorder.Recent().Count).IsEqualTo(0);
	}

	[Test]
	public async Task WaitAndExecutionUseMonotonicTimeAndCompletionIsRecordedOnce()
	{
		var clock = new Clock(); var recorder = new QueueDiagnosticsRecorder(clock);
		var entry = recorder.Admitted(1, new DBRef(3, 100), new DBRef(2, 100), "delay", "ACTION");
		clock.Advance(TimeSpan.FromSeconds(5));
		using (entry.Enter())
		{
			clock.Utc -= TimeSpan.FromHours(1);
			clock.Advance(TimeSpan.FromSeconds(2));
			recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 1, true));
			recorder.RecordInvocation(new(TelemetryInvocationKind.Command, "think", 2, false));
			entry.Complete(QueueOutcome.Completed);
		}
		entry.Complete(QueueOutcome.Cancelled);
		var row = recorder.Recent().Single();
		await Assert.That(row.WaitDuration).IsEqualTo(TimeSpan.FromSeconds(5));
		await Assert.That(row.ExecutionDuration).IsEqualTo(TimeSpan.FromSeconds(2));
		await Assert.That(row.InvocationCount).IsEqualTo(2L);
		await Assert.That(row.FailedInvocations).IsEqualTo(1L);
		await Assert.That(row.Outcome).IsEqualTo(QueueOutcome.InvocationFailure);
		await Assert.That(row.SourceAttribute).IsEqualTo("ACTION");
		clock.Advance(TimeSpan.FromMinutes(1));
		await Assert.That(entry.ExecutionDuration).IsEqualTo(TimeSpan.FromSeconds(2));
	}

	[Test]
	public async Task NestedObservationRestoresTheCallingQueueCounter()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var outer = recorder.Admitted(1, null, null, "enqueue");
		var inner = recorder.Admitted(2, null, null, "enqueue");
		using (outer.Enter())
		{
			recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 1, true));
			using (inner.Enter()) recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "sub", 1, true));
			recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "mul", 1, true));
		}
		await Assert.That(outer.InvocationCount).IsEqualTo(2L);
		await Assert.That(inner.InvocationCount).IsEqualTo(1L);
	}

	[Test]
	public async Task UnknownAndRejectedAttributionNeverInventsAnIdentityOrRetainsInput()
	{
		var recorder = new QueueDiagnosticsRecorder();
		const string secret = "connect account private-password";
		recorder.Rejected(new DBRef(1), null, secret, QueueOutcome.InvalidTarget);
		var row = recorder.Recent().Single();
		await Assert.That(row.Source).IsNull();
		await Assert.That(row.Owner).IsNull();
		await Assert.That(row.Kind).IsEqualTo("other");
		await Assert.That(row.ExecutionDuration).IsNull();
		await Assert.That(JsonSerializer.Serialize(row).Contains(secret, StringComparison.Ordinal)).IsFalse();
	}
}
