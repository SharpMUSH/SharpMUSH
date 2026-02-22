using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Service that reconciles connection state from Redis on startup.
/// Rebuilds the in-memory connection list from the shared Redis state store.
/// </summary>
public class ConnectionReconciliationService : IHostedService
{
	private readonly IConnectionService _connectionService;
	private readonly IMessageBus _bus;
	private readonly ILogger<ConnectionReconciliationService> _logger;

	public ConnectionReconciliationService(
		IConnectionService connectionService,
		IMessageBus bus,
		ILogger<ConnectionReconciliationService> logger)
	{
		_connectionService = connectionService;
		_bus = bus;
		_logger = logger;
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting connection state reconciliation from Redis...");

		try
		{
			// Reconcile state from Redis
			await _connectionService.ReconcileFromStateStoreAsync(
				handle => async data => await _bus.Publish(new TelnetOutputMessage(handle, data), cancellationToken),
				handle => async data => await _bus.Publish(new TelnetPromptMessage(handle, data), cancellationToken),
				() => System.Text.Encoding.UTF8
			);

			_logger.LogInformation("Connection state reconciliation completed successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to reconcile connection state from Redis");
			// Don't throw - allow the application to start even if reconciliation fails
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		// No cleanup needed
		return Task.CompletedTask;
	}
}
