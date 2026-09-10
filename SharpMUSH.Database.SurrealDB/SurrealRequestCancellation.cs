namespace SharpMUSH.Database.SurrealDB;

internal static class SurrealRequestCancellation
{
	internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!cancellationToken.CanBeCanceled)
		{
			return await request(cancellationToken);
		}

		// Embedded SDK 0.9.0 leaves registrations pointing at disposed query timeout
		// sources. Give each request its own token, and detach it from the caller
		// before disposing it, so long-lived server tokens retain no completed queries.
		using var requestCancellation = new CancellationTokenSource();
		using var registration = cancellationToken.Register(static state =>
		{
			try
			{
				((CancellationTokenSource)state!).Cancel();
			}
			catch (AggregateException exception) when (
				exception.Flatten().InnerExceptions.All(error => error is ObjectDisposedException))
			{
				// A request can finish while cancellation is being forwarded. Cancel
				// still invokes every callback; only the SDK's disposed callbacks are ignored.
			}
		}, requestCancellation);
		return await request(requestCancellation.Token);
	}

	internal static async Task RunAsync(Func<CancellationToken, Task> request, CancellationToken cancellationToken)
		=> await RunAsync(async token => { await request(token); return true; }, cancellationToken);

}
