using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handles connection state change notifications and triggers corresponding PennMUSH-compatible events.
/// <para>
/// This handler listens for <see cref="ConnectionStateChangeNotification"/> messages and triggers
/// appropriate event attributes on the configured event handler object:
/// </para>
/// <list type="bullet">
/// <item><description>SOCKET`CONNECT - When a socket connects (args: descriptor, ip)</description></item>
/// <item><description>PLAYER`DISCONNECT - When a player disconnects (args: objid, remaining connections, hidden, cause, ip, descriptor, conn secs, idle secs, stats)</description></item>
/// <item><description>SOCKET`DISCONNECT - When a socket disconnects (args: descriptor, ip, cause, stats)</description></item>
/// </list>
/// </summary>
public class ConnectionStateEventHandler(
	IConnectionService connectionService,
	IEventService eventService,
	IMUSHCodeParser parser)
	: INotificationHandler<ConnectionStateChangeNotification>
{
	public async ValueTask Handle(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		// Trigger SOCKET`CONNECT when a new socket connects
		// PennMUSH spec: socket`connect (descriptor, ip)
		if (notification.OldState == IConnectionService.ConnectionState.None && 
		    notification.NewState == IConnectionService.ConnectionState.Connected)
		{
			var connectionData = connectionService.Get(notification.Handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip) 
					? ip : "unknown";
				
				// EventService handles all exception logging, so no try-catch needed here
				await eventService.TriggerEventAsync(
					parser,
					"SOCKET`CONNECT",
					null, // System event (no enactor)
					notification.Handle.ToString(),
					ipAddress);
			}
		}
		
		// Trigger PLAYER`DISCONNECT when a logged-in player disconnects
		// PennMUSH spec: player`disconnect (objid, number of remaining connections, hidden?, cause of disconnection, ip, descriptor, conn() secs, idle() secs, recv bytes/sent bytes/command count)
		if (notification.OldState == IConnectionService.ConnectionState.LoggedIn && 
		    notification.NewState == IConnectionService.ConnectionState.Disconnected &&
		    notification.PlayerRef.HasValue)
		{
			var connectionData = connectionService.Get(notification.Handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip) 
					? ip : "unknown";
				
				// Calculate remaining connections for this player
				var remainingConnections = await connectionService.Get(notification.PlayerRef.Value).CountAsync();
				
				// Calculate connection statistics
				var connectedSecs = connectionData.Connected?.TotalSeconds.ToString("F0") ?? "0";
				var idleSecs = connectionData.Idle?.TotalSeconds.ToString("F0") ?? "0";
				var bytesRecv = connectionData.Metadata.TryGetValue("BytesReceived", out var recv) ? recv : "0";
				var bytesSent = connectionData.Metadata.TryGetValue("BytesSent", out var sent) ? sent : "0";
				var commandCount = connectionData.Metadata.TryGetValue("CommandCount", out var count) ? count : "0";
				
				// Trigger PLAYER`DISCONNECT event
				await eventService.TriggerEventAsync(
					parser,
					"PLAYER`DISCONNECT",
					notification.PlayerRef.Value, // Enactor is the disconnecting player
					$"#{notification.PlayerRef.Value.Number}",
					remainingConnections.ToString(),
					"0", // hidden? (0 = not hidden, 1 = hidden)
					"quit", // cause of disconnection
					ipAddress,
					notification.Handle.ToString(),
					connectedSecs,
					idleSecs,
					$"{bytesRecv}/{bytesSent}/{commandCount}");
			}
		}
		
		// Trigger SOCKET`DISCONNECT when a socket disconnects
		// PennMUSH spec: socket`disconnect (former descriptor, former ip, cause of disconnection, recv bytes/sent bytes/command count)
		if (notification.NewState == IConnectionService.ConnectionState.Disconnected)
		{
			var connectionData = connectionService.Get(notification.Handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip) 
					? ip : "unknown";
				
				// Calculate statistics following PennMUSH format
				var bytesRecv = connectionData.Metadata.TryGetValue("BytesReceived", out var recv) ? recv : "0";
				var bytesSent = connectionData.Metadata.TryGetValue("BytesSent", out var sent) ? sent : "0";
				var commandCount = connectionData.Metadata.TryGetValue("CommandCount", out var count) ? count : "0";
				
				// EventService handles all exception logging
				await eventService.TriggerEventAsync(
					parser,
					"SOCKET`DISCONNECT",
					null, // System event (no enactor)
					notification.Handle.ToString(),
					ipAddress,
					"disconnect", // cause of disconnection
					$"{bytesRecv}/{bytesSent}/{commandCount}"); // PennMUSH format: recv/sent/count
			}
		}
	}
}
