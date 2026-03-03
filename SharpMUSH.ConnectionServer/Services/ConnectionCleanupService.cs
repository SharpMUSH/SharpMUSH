using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Hosted service that purges stale connection state from the shared state store
/// when the ConnectionServer starts. This ensures a clean slate—any entries left
/// from a previous run are removed because the actual TCP/WebSocket connections
/// they referenced no longer exist.
/// </summary>
public class ConnectionCleanupService(
	IConnectionStateStore stateStore,
	ILogger<ConnectionCleanupService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Purging stale connection state from shared state store on startup.");

		try
		{
			var handles = await stateStore.GetAllHandlesAsync(cancellationToken);
			var handleList = handles.ToList();

			if (handleList.Count == 0)
			{
				logger.LogInformation("No stale connection state found.");
				return;
			}

			logger.LogInformation("Found {Count} stale connection entries to remove.", handleList.Count);

			foreach (var handle in handleList)
			{
				try
				{
					await stateStore.RemoveConnectionAsync(handle, cancellationToken);
					logger.LogDebug("Removed stale connection state for handle {Handle}.", handle);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to remove stale connection state for handle {Handle}.", handle);
				}
			}

			logger.LogInformation("Stale connection state purge completed. Removed {Count} entries.", handleList.Count);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to purge stale connection state on startup.");
			// Don't throw—allow the application to start even if cleanup fails
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
