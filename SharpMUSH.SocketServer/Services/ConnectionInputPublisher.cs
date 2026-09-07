using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Services;

public static class ConnectionInputPublisher
{
	public static async Task PublishAsync<T>(IMessageBus bus, IConnectionServerService connections,
		ILogger logger, long handle, T message, CancellationToken ct) where T : class
	{
		try
		{
			await bus.Publish(message, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			// An acknowledgement may have been lost. Automatically retrying could execute a command twice.
			logger.LogWarning(ex, "Input delivery could not be confirmed for {Handle}; retaining the socket", handle);
			var connection = connections.Get(handle);
			if (connection is null) return;
			try
			{
				await connection.OutputFunction(connection.EncodingFunction().GetBytes(
					"\r\n[SharpMUSH] Command delivery could not be confirmed. Please wait for recovery and check before retrying.\r\n"))
					.AsTask().WaitAsync(TimeSpan.FromSeconds(2), ct);
			}
			catch (Exception noticeError) when (noticeError is not OperationCanceledException || !ct.IsCancellationRequested)
			{
				logger.LogDebug(noticeError, "Could not send input failure notice to {Handle}", handle);
			}
		}
	}
}
