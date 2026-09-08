using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

public class NatsConsumerGroupTests
{
	[Test]
	public async Task ConsumerFailureCancelsAndDrainsHealthySiblingBeforePropagating()
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var drained = false;
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		await Assert.That(async () => await NatsJetStreamConsumerService.RunConsumersAsync(
		[
			async ct =>
			{
				started.SetResult();
				try { await Task.Delay(Timeout.Infinite, ct); }
				finally { drained = true; }
			},
			async _ => { await started.Task; throw new IOException("broker failed"); }
		], timeout.Token)).Throws<IOException>();

		await Assert.That(drained).IsTrue();
	}

	[Test]
	public async Task ShutdownCancelsEveryConsumerWithoutWaitingForAnotherFailure()
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var shutdown = new CancellationTokenSource();
		var drained = 0;
		async Task Consume(CancellationToken ct)
		{
			started.TrySetResult();
			try { await Task.Delay(Timeout.Infinite, ct); }
			finally { Interlocked.Increment(ref drained); }
		}
		var running = NatsJetStreamConsumerService.RunConsumersAsync([Consume, Consume], shutdown.Token);
		await started.Task;
		await shutdown.CancelAsync();

		await Assert.That(async () => await running.WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();
		await Assert.That(drained).IsEqualTo(2);
	}

	[Test]
	public async Task UnexpectedConsumerCompletionTriggersRecovery()
	{
		await Assert.That(async () => await NatsJetStreamConsumerService.RunConsumersAsync(
			[_ => Task.CompletedTask], CancellationToken.None)).Throws<IOException>();
	}

	[Test]
	[Arguments(0, 0.5, 1.0)]
	[Arguments(1, 1.0, 2.0)]
	[Arguments(5, 15.0, 30.0)]
	[Arguments(int.MaxValue, 15.0, 30.0)]
	public async Task RecoveryDelayUsesBoundedExponentialJitter(int failures, double minimum, double maximum)
	{
		for (var i = 0; i < 100; i++)
		{
			var delay = NatsJetStreamConsumerService.RetryDelay(failures).TotalSeconds;
			await Assert.That(delay >= minimum && delay <= maximum).IsTrue();
		}
	}
}
