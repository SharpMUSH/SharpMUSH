using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Service that reconciles connection state from NATS KV on startup.
/// Rebuilds the in-memory connection list from the shared NATS KV state store.
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
		_logger.LogInformation("Starting connection state reconciliation from NATS KV...");

		try
		{
			// The delegates below outlive this call by the whole life of the restored connection, so
			// they must not close over the startup token: its source is disposed once StartAsync
			// returns, and a configured HostOptions.StartupTimeout would cancel it outright — either
			// way every byte the game ever sends a player who was connected before the restart would
			// fail on a token that stopped meaning anything the moment the host finished starting.
			await _connectionService.ReconcileFromStateStoreAsync(
				handle => async data => await _bus.Publish(new TelnetOutputMessage(handle, data), CancellationToken.None),
				handle => async data => await _bus.Publish(new TelnetPromptMessage(handle, data), CancellationToken.None),
				() => System.Text.Encoding.UTF8
			);

			_logger.LogInformation("Connection state reconciliation completed successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to reconcile connection state from NATS KV");
			// Never announce readiness with missing player bindings.
			throw;
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
