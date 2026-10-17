using System.Diagnostics;
using NATS.Client.Core;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

public class NatsStartupConnectionLifetimeTests
{
	[Test]
	public Task CallerCancellationDrainsTheActualConnectInvocation() => AssertFailedStartupDrainsConnectAsync(FailureCause.CallerCancellation);

	[Test]
	public Task DeadlineDrainsConnectAndRemainsTimeoutWhenCallerCancelsDuringCleanup() => AssertFailedStartupDrainsConnectAsync(FailureCause.Deadline);

	[Test]
	public Task InvalidDeadlineDrainsTheActualConnectInvocation() => AssertFailedStartupDrainsConnectAsync(FailureCause.InvalidDeadline);

	[Test]
	public async Task OriginalConnectFailureIsPreserved()
	{
		await using var nats = new NatsConnection(new NatsOpts { Url = "nats://127.0.0.1:1", RetryOnInitialConnect = false });
		var connectTask = nats.ConnectAsync().AsTask();
		var error = await Assert.That(async () => await NatsStartupConnection.ConnectOwnedAsync(
			nats, connectTask, TimeSpan.FromSeconds(5), default)).Throws<NatsException>();
		await Assert.That(error).IsSameReferenceAs(connectTask.Exception?.InnerException);
	}

	[Test]
	public async Task UnreachableBrokerHonorsCallerCancellation()
	{
		using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
		var stopwatch = Stopwatch.StartNew();
		await Assert.That(async () => await NatsStartupConnection.ConnectAsync(
			"nats://127.0.0.1:1", TimeSpan.FromSeconds(10), caller.Token)).Throws<OperationCanceledException>();
		await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
	}

	private static async Task AssertFailedStartupDrainsConnectAsync(FailureCause cause)
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await using var nats = new DisposalTrackedConnection(new NatsOpts
		{
			Url = "nats://127.0.0.1:1", RetryOnInitialConnect = true
		});
		nats.OnConnectingAsync = async endpoint =>
		{
			entered.TrySetResult();
			await release.Task;
			return endpoint;
		};
		using var caller = new CancellationTokenSource();
		var connectTask = nats.ConnectAsync().AsTask();
		var timeout = cause switch
		{
			FailureCause.Deadline => TimeSpan.FromMilliseconds(100),
			FailureCause.InvalidDeadline => TimeSpan.MaxValue,
			_ => TimeSpan.FromSeconds(10)
		};
		var owned = NatsStartupConnection.ConnectOwnedAsync(nats, connectTask, timeout, caller.Token);
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (cause == FailureCause.CallerCancellation)
				caller.Cancel();
			if (cause != FailureCause.InvalidDeadline)
				await nats.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
			// The callback deliberately keeps the real client invocation alive. Cleanup must own it
			// until it exits, even though the deadline wrapper has already been canceled.
			await Task.WhenAny(owned, Task.Delay(TimeSpan.FromMilliseconds(100)));
			await Assert.That(owned.IsCompleted).IsFalse();
			if (cause == FailureCause.InvalidDeadline)
				await nats.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
			// Once timeout cleanup has started, later caller cancellation must not reclassify it.
			caller.Cancel();
		}
		finally
		{
			release.TrySetResult();
			caller.Cancel();
			try
			{
				// Also stop the real client if a regression skipped owner cleanup. Watchdogs
				// above detect failures; cleanup must still drain the actual tasks afterward.
				await nats.DisposeAsync();
			}
			finally
			{
				try
				{
					await owned;
				}
				catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
				{
				}
				finally
				{
					try
					{
						await connectTask;
					}
					catch (Exception ex) when (ex is NatsException or ObjectDisposedException or OperationCanceledException)
					{
					}
				}
			}
		}

		if (cause == FailureCause.InvalidDeadline)
			await Assert.That(async () => await owned).Throws<ArgumentOutOfRangeException>();
		else if (cause == FailureCause.Deadline)
			await Assert.That(async () => await owned).Throws<TimeoutException>();
		else
			await Assert.That(async () => await owned).Throws<OperationCanceledException>();
		await Assert.That(connectTask.IsCompleted).IsTrue();
	}

	private enum FailureCause { CallerCancellation, Deadline, InvalidDeadline }

	private sealed class DisposalTrackedConnection(NatsOpts opts) : NatsConnection(opts)
	{
		private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public Task Disposed => _disposed.Task;

		public override async ValueTask DisposeAsync()
		{
			await base.DisposeAsync();
			_disposed.TrySetResult();
		}
	}
}
