using NATS.Client.Core;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Opens the NATS connection a host needs before it can start. A single connect attempt can miss its
/// window while the broker or the machine is busy, so failed attempts are retried by the client's
/// reconnect loop until <paramref name="timeout"/> (or the caller's token) ends the wait.
/// Failure cleanup disposes the connection and drains its connect invocation before returning;
/// the timeout bounds the initial wait, not the time spent completing that cleanup.
/// </summary>
public static class NatsStartupConnection
{
	/// <summary>Default overall deadline for the first connection to a broker.</summary>
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

	public static async Task<NatsConnection> ConnectAsync(string url, TimeSpan timeout, CancellationToken ct = default)
	{
		if (timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeout));
		var nats = new NatsConnection(new NatsOpts { Url = url, RetryOnInitialConnect = true });
		return await ConnectOwnedAsync(nats, nats.ConnectAsync().AsTask(), timeout, ct);
	}

	// Takes ownership of this connection and its once-started connect invocation until a successful
	// handoff. Every failure, including deadline setup, must dispose and join before returning.
	internal static async Task<NatsConnection> ConnectOwnedAsync(
		NatsConnection nats, Task connectTask, TimeSpan timeout, CancellationToken ct)
	{
		try
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
			deadline.CancelAfter(timeout);
			await connectTask.WaitAsync(deadline.Token);
			return nats;
		}
		catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
		{
			var error = new TimeoutException($"Could not connect to NATS at {nats.Opts.Url} within {timeout}.", ex);
			await CleanupAsync(nats, connectTask, error);
			throw error;
		}
		catch (Exception ex)
		{
			await CleanupAsync(nats, connectTask, ex);
			throw;
		}
	}

	private static async Task CleanupAsync(NatsConnection nats, Task connectTask, Exception error)
	{
		try
		{
			await nats.DisposeAsync();
		}
		catch (Exception ex)
		{
			// Cleanup is secondary to the timeout, cancellation or original connection failure.
			error.Data["NatsDisposalException"] = ex;
		}
		finally
		{
			try
			{
				// WaitAsync cancels only the wait. Disposal stops retries, but the original
				// ConnectAsync invocation still has to finish and its exception must be observed.
				await connectTask;
			}
			catch (Exception ex)
			{
				if (!ReferenceEquals(error, ex))
					error.Data["NatsConnectCleanupException"] = ex;
			}
		}
	}
}
