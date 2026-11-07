using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Server.Handlers;

/// <summary>
/// Handles connection state changes and sends appropriate messages to the client.
/// </summary>
public class ConnectionStateChangeHandler(
	ILogger<ConnectionStateChangeHandler> logger,
	IConnectionService connectionService,
	IAttributeStore attributes,
	IMessageBus messageBus)
	: INotificationHandler<ConnectionStateChangeNotification>
{
	public async ValueTask Handle(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		var connectionId = Guid.NewGuid().ToString("N")[..8];

		logger.LogInformation(
			"[{ConnectionId}] Connection state change: Handle={Handle}, Ref={Ref}, OldState={OldState}, NewState={NewState}",
			connectionId, notification.Handle, notification.PlayerRef, notification.OldState, notification.NewState);

		var connection = connectionService.Get(notification.Handle);
		if (connection is null && notification.NewState != IConnectionService.ConnectionState.Disconnected)
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
					if (notification.OldState == IConnectionService.ConnectionState.LoggedIn)
					{
						// The socket survives LOGOUT, but the character's flags do not. Clear them before
						// rendering the connect screen so negotiated terminal capabilities take over again.
						await messageBus.Publish(
							new ClearPlayerOutputPreferencesMessage(notification.Handle), cancellationToken);
					}
					break;

				case IConnectionService.ConnectionState.LoggedIn:
					logger.LogInformation("[{ConnectionId}] Player {Ref} logged in on handle {Handle}",
						connectionId, notification.PlayerRef, notification.Handle);

					if (notification.PlayerRef.HasValue)
					{
						var localeAttrs = attributes.GetAttributeAsync(notification.PlayerRef.Value, ["LOCALE"], cancellationToken);
						await foreach (var attr in localeAttrs)
						{
							var savedLocale = attr.Value.ToPlainText();
							if (!string.IsNullOrWhiteSpace(savedLocale))
							{
								connectionService.Update(notification.Handle, "Locale", savedLocale);
							}
						}
					}
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
