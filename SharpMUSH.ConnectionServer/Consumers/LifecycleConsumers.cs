using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Handles MainProcessReadyMessage from the Server.
/// The Server already rebuilds its connection state from the shared NATS KV store
/// via <c>ConnectionReconciliationService</c> on startup, so this consumer only
/// logs the event. Re-publishing <c>ConnectionEstablishedMessage</c> would trigger
/// unwanted side effects (welcome messages, state resets, lost player bindings).
/// </summary>
public class MainProcessReadyConsumer(
	IConnectionServerService connectionService,
	ILogger<MainProcessReadyConsumer> logger)
	: IMessageConsumer<MainProcessReadyMessage>
{
	public Task HandleAsync(MainProcessReadyMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogInformation(
			"[LIFECYCLE] MainProcess is ready (Version: {Version}, Timestamp: {Timestamp}). " +
			"ConnectionServer has {Count} active connections. " +
			"Server will reconcile state from the shared state store.",
			message.Version, message.Timestamp, connectionService.GetAll().Count());

		return Task.CompletedTask;
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
