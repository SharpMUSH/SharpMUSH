using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Server.Handlers;

/// <summary>
/// Handles connection state changes and sends appropriate messages to the client.
/// </summary>
public class ConnectionStateChangeHandler(
	ILogger<ConnectionStateChangeHandler> logger,
	IConnectionService connectionService)
	: INotificationHandler<ConnectionStateChangeNotification>
{
	public async ValueTask Handle(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		var connectionId = Guid.NewGuid().ToString("N")[..8]; // Unique ID for tracking

		logger.LogInformation(
			"[{ConnectionId}] Connection state change: Handle={Handle}, Ref={Ref}, OldState={OldState}, NewState={NewState}",
			connectionId, notification.Handle, notification.PlayerRef, notification.OldState, notification.NewState);

		var connection = connectionService.Get(notification.Handle);
		if (connection is null)
		{
			logger.LogWarning("[{ConnectionId}] Connection {Handle} not found in service",
				connectionId, notification.Handle);
			return;
		}

		try
		{
			switch (notification.NewState)
			{
				case IConnectionService.ConnectionState.Connected:
					logger.LogInformation("[{ConnectionId}] Sending 'Connected!' message to handle {Handle}",
						connectionId, notification.Handle);

					var welcomeMessage = Encoding.UTF8.GetBytes("Connected!\r\n");
					await connection.OutputFunction(welcomeMessage);

					logger.LogInformation("[{ConnectionId}] Successfully sent welcome message to handle {Handle}",
						connectionId, notification.Handle);
					break;

				case IConnectionService.ConnectionState.LoggedIn:
					logger.LogInformation("[{ConnectionId}] Player {Ref} logged in on handle {Handle}",
						connectionId, notification.PlayerRef, notification.Handle);

					var loginMessage = Encoding.UTF8.GetBytes($"Welcome back, {notification.PlayerRef}!\r\n");
					await connection.OutputFunction(loginMessage);

					logger.LogInformation("[{ConnectionId}] Successfully sent login message to handle {Handle}",
						connectionId, notification.Handle);
					break;

				case IConnectionService.ConnectionState.Disconnected:
					logger.LogInformation("[{ConnectionId}] Handle {Handle} disconnected",
						connectionId, notification.Handle);
					break;

				default:
					logger.LogWarning("[{ConnectionId}] Unknown connection state: {State}",
						connectionId, notification.NewState);
					break;
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "[{ConnectionId}] Error handling connection state change for handle {Handle}",
				connectionId, notification.Handle);
		}
	}
}
