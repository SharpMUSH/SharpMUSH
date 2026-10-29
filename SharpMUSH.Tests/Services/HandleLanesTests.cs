using System.Collections.Concurrent;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

public class HandleLanesTests
{
	[Test]
	public async Task AnotherHandleRunsWhileOneHandleIsBlocked()
	{
		using var lanes = new HandleLanes(8);
		var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var otherRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await lanes.DispatchAsync(1, () => blocked.Task, CancellationToken.None);
		await lanes.DispatchAsync(2, () => { otherRan.SetResult(); return Task.CompletedTask; }, CancellationToken.None);

		await otherRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
		blocked.SetResult();
		await lanes.DrainAsync();
	}

	[Test]
	public async Task OneHandleRunsItsWorkInDispatchOrder()
	{
		using var lanes = new HandleLanes(64);
		var seen = new ConcurrentQueue<int>();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await lanes.DispatchAsync(7, () => gate.Task, CancellationToken.None);
		for (var i = 0; i < 50; i++)
		{
			var n = i;
			await lanes.DispatchAsync(7, async () => { await Task.Yield(); seen.Enqueue(n); }, CancellationToken.None);
		}
		gate.SetResult();
		await lanes.DrainAsync();

		await Assert.That(string.Join(",", seen)).IsEqualTo(string.Join(",", Enumerable.Range(0, 50)));
	}

	[Test]
	public async Task AFailureSkipsWorkNotYetStartedAndIsRethrownOnDrain()
	{
		using var lanes = new HandleLanes(8);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var laterRan = false;

		await lanes.DispatchAsync(3, async () => { await gate.Task; throw new IOException("ack failed"); }, CancellationToken.None);
		await lanes.DispatchAsync(3, () => { laterRan = true; return Task.CompletedTask; }, CancellationToken.None);
		gate.SetResult();

		await Assert.That(lanes.DrainAsync).Throws<IOException>();
		await Assert.That(lanes.Faulted.IsCancellationRequested).IsTrue();
		await Assert.That(laterRan).IsFalse();
	}

	[Test]
	public async Task DispatchWaitsForASlotOnceCapacityIsInFlight()
	{
		using var lanes = new HandleLanes(1);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await lanes.DispatchAsync(1, () => gate.Task, CancellationToken.None);
		var second = lanes.DispatchAsync(2, () => Task.CompletedTask, CancellationToken.None).AsTask();

		await Assert.That(second.IsCompleted).IsFalse();
		gate.SetResult();
		await second.WaitAsync(TimeSpan.FromSeconds(5));
		await lanes.DrainAsync();
	}

	[Test]
	public async Task HandleIsReadFromThePayload()
	{
		using var document = System.Text.Json.JsonDocument.Parse("""{"Handle":42,"Markup":"x"}""");
		await Assert.That(NatsJetStreamConsumerService.HandleOf(document.RootElement)).IsEqualTo(42L);
		using var none = System.Text.Json.JsonDocument.Parse("\"text\"");
		await Assert.That(NatsJetStreamConsumerService.HandleOf(none.RootElement)).IsEqualTo(0L);
		using var quoted = System.Text.Json.JsonDocument.Parse("""{"Handle":"42"}""");
		await Assert.That(NatsJetStreamConsumerService.HandleOf(quoted.RootElement)).IsEqualTo(0L);
		using var missing = System.Text.Json.JsonDocument.Parse("""{"Handle":null}""");
		await Assert.That(NatsJetStreamConsumerService.HandleOf(missing.RootElement)).IsEqualTo(0L);
	}
}
