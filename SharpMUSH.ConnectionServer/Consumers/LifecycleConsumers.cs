using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Handles MainProcessReadyMessage from the Server.
/// When the Server (re)starts, re-publishes all active connections so the Server
/// can rebuild its in-memory state without dropping any live sockets.
/// </summary>
public class MainProcessReadyConsumer(
	IConnectionServerService connectionService,
	IConnectionStateStore stateStore,
	IMessageBus messageBus,
	ILogger<MainProcessReadyConsumer> logger)
	: IMessageConsumer<MainProcessReadyMessage>
{
	public async Task HandleAsync(MainProcessReadyMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogInformation(
			"[LIFECYCLE] MainProcess is ready (Version: {Version}, Timestamp: {Timestamp}). Re-syncing {Count} active connections.",
			message.Version, message.Timestamp, connectionService.GetAll().Count());

		// Re-publish ConnectionEstablishedMessage for every live connection so the
		// Server can rebuild its connection table after a restart.
		foreach (var connection in connectionService.GetAll())
		{
			try
			{
				// Retrieve original connection metadata from the state store
				var stateData = await stateStore.GetConnectionAsync(connection.Handle, cancellationToken);
				var ipAddress = stateData?.IpAddress ?? "unknown";
				var hostname = stateData?.Hostname ?? "unknown";
				var connectionType = stateData?.ConnectionType ?? "unknown";

				await messageBus.Publish(new ConnectionEstablishedMessage(
					connection.Handle,
					ipAddress,
					hostname,
					connectionType,
					DateTimeOffset.UtcNow
				), cancellationToken);

				logger.LogDebug("[LIFECYCLE] Re-published ConnectionEstablished for handle {Handle}", connection.Handle);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "[LIFECYCLE] Failed to re-publish ConnectionEstablished for handle {Handle}", connection.Handle);
			}
		}
	}
}

/// <summary>
/// Handles MainProcessShutdownMessage from the Server.
/// ConnectionServer keeps all socket connections alive; clients stay connected
/// and can resume once the Server restarts.
/// </summary>
public class MainProcessShutdownConsumer(
	ILogger<MainProcessShutdownConsumer> logger)
	: IMessageConsumer<MainProcessShutdownMessage>
{
	public Task HandleAsync(MainProcessShutdownMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogWarning(
			"[LIFECYCLE] MainProcess is shutting down (Reason: {Reason}, Timestamp: {Timestamp}). " +
			"Keeping all connections alive for reconnection.",
			message.Reason ?? "None", message.Timestamp);

		return Task.CompletedTask;
	}
}
