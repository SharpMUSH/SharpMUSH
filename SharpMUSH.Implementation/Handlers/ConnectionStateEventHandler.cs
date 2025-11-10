using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handles connection state change notifications and triggers corresponding events.
/// </summary>
public class ConnectionStateEventHandler(
	IConnectionService connectionService,
	IEventService eventService,
	IMUSHCodeParser parser,
	ILogger<ConnectionStateEventHandler> logger)
	: INotificationHandler<ConnectionStateChangeNotification>
{
	public async ValueTask Handle(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		// Trigger SOCKET`CONNECT when a new socket connects
		if (notification.OldState == IConnectionService.ConnectionState.None && 
		    notification.NewState == IConnectionService.ConnectionState.Connected)
		{
			// Get connection info
			var connectionData = connectionService.Get(notification.Handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip) 
					? ip : "unknown";
				
				try
				{
					await eventService.TriggerEventAsync(
						parser,
						"SOCKET`CONNECT",
						null, // System event (no enactor)
						notification.Handle.ToString(),
						ipAddress);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error triggering SOCKET`CONNECT event");
				}
			}
		}
		
		// Trigger SOCKET`DISCONNECT when a socket disconnects
		if (notification.NewState == IConnectionService.ConnectionState.Disconnected)
		{
			// Get connection info before it's removed
			var connectionData = connectionService.Get(notification.Handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip) 
					? ip : "unknown";
				
				// Calculate statistics
				var connectedTime = connectionData.Connected?.TotalSeconds.ToString("F0") ?? "0";
				var bytesRecv = connectionData.Metadata.TryGetValue("BytesReceived", out var recv) ? recv : "0";
				var bytesSent = connectionData.Metadata.TryGetValue("BytesSent", out var sent) ? sent : "0";
				var commandCount = connectionData.Metadata.TryGetValue("CommandCount", out var count) ? count : "0";
				
				try
				{
					await eventService.TriggerEventAsync(
						parser,
						"SOCKET`DISCONNECT",
						null, // System event (no enactor)
						notification.Handle.ToString(),
						ipAddress,
						"disconnect", // cause - simplified for now
						$"{bytesRecv}/{bytesSent}/{commandCount}");
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error triggering SOCKET`DISCONNECT event");
				}
			}
		}
	}
}
