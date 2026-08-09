using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Handlers;

/// <summary>
/// Handles connection state changes and sends appropriate messages to the client.
/// </summary>
public class ConnectionStateChangeHandler(
	ILogger<ConnectionStateChangeHandler> logger,
	IConnectionService connectionService,
	INotifyService notifyService,
	ISharpDatabase database)
	: INotificationHandler<ConnectionStateChangeNotification>
{
	public async ValueTask Handle(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		var connectionId = Guid.NewGuid().ToString("N")[..8];

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

					await notifyService.NotifyLocalized(notification.Handle, nameof(ErrorMessages.Notifications.Connected));

					logger.LogInformation("[{ConnectionId}] Successfully sent welcome message to handle {Handle}",
						connectionId, notification.Handle);
					break;

				case IConnectionService.ConnectionState.LoggedIn:
					logger.LogInformation("[{ConnectionId}] Player {Ref} logged in on handle {Handle}",
						connectionId, notification.PlayerRef, notification.Handle);

					if (notification.PlayerRef.HasValue)
					{
						var localeAttrs = database.GetAttributeAsync(notification.PlayerRef.Value, ["LOCALE"], cancellationToken);
						await foreach (var attr in localeAttrs)
						{
							var savedLocale = attr.Value.ToPlainText();
							if (!string.IsNullOrWhiteSpace(savedLocale))
							{
								connectionService.Update(notification.Handle, "Locale", savedLocale);
							}
						}
					}

					await GreetAsync(notification, cancellationToken);

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

	/// <summary>
	/// The first line of every session. It is addressed to the player by NAME — passing the
	/// <see cref="DBRef"/> straight in as the format argument rendered its <c>ToString()</c>, so
	/// players were greeted with "Welcome back, #12:1786227218582!".
	/// </summary>
	private async ValueTask GreetAsync(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		var name = await ResolveNameAsync(notification.PlayerRef, cancellationToken);
		if (name is null)
		{
			// Better to say nothing than to greet somebody by database key.
			logger.LogWarning("Could not resolve a name for {Ref} on handle {Handle}; skipping the greeting",
				notification.PlayerRef, notification.Handle);
			return;
		}

		await notifyService.NotifyLocalized(notification.Handle,
			nameof(ErrorMessages.Notifications.WelcomeBackFormat), name);
	}

	private async ValueTask<string?> ResolveNameAsync(DBRef? playerRef, CancellationToken cancellationToken)
	{
		if (!playerRef.HasValue) return null;

		var node = await database.GetObjectNodeAsync(playerRef.Value, cancellationToken);
		return node.IsNone() ? null : node.Known().Object().Name;
	}
}
