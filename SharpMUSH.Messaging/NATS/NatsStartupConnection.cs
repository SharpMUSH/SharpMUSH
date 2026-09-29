using NATS.Client.Core;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Opens the NATS connection a host needs before it can start. A single connect attempt can miss its
/// window while the broker or the machine is busy, so failed attempts are retried by the client's
/// reconnect loop until <paramref name="timeout"/> (or the caller's token) ends the wait.
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
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(timeout);
		try
		{
			await nats.ConnectAsync().AsTask().WaitAsync(deadline.Token);
			return nats;
		}
		catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
		{
			await nats.DisposeAsync();
			throw new TimeoutException($"Could not connect to NATS at {url} within {timeout}.", ex);
		}
		catch
		{
			await nats.DisposeAsync();
			throw;
		}
	}
}
