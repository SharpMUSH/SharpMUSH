using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Messaging.Messages;

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
	INotifyService notifyService,
	IMediator mediator,
	IConnectionAnnounceService connectionAnnounceService,
	IGameMessageService gameMessages,
	ILogger<ConnectionStateEventHandler> logger)
	: INotificationHandler<ConnectionStateChangeNotification>
{
	public async ValueTask Handle(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		// A socket closing is handled outside the queue's consumer. The events and hooks below run once
		// the whole change is recorded — LASTLOGOUT above all — as PennMUSH's queue entries do.
		await using var hold = QueueHold.Enter();

		// The connect screen goes to any connection arriving at the login prompt — a brand new socket,
		// and equally one returning there via LOGOUT. PennMUSH logout_sock finishes with
		// welcome_user(d, 0), the same call a fresh connection gets, because the descriptor is meant to
		// look untouched afterwards.
		if (notification.NewState == IConnectionService.ConnectionState.Connected)
		{
			var connectionData = connectionService.Get(notification.Handle);
			if (connectionData != null)
			{
				// A portal terminal logs in the moment it opens; the screen explaining how to log in is not for it.
				// After a LOGOUT it is at the login prompt like any other connection.
				var signingIn = notification.OldState == IConnectionService.ConnectionState.None
					&& connectionData.Metadata.GetValueOrDefault(ConnectionEstablishedMessage.SignInMetadata) == "1";
				if (!signingIn && await gameMessages.RenderAsync(GameMessage.Connect, notification.Handle) is MString connectText)
				{
					await notifyService.Notify(notification.Handle, connectText);
				}

				// SOCKET`CONNECT is about the socket, not the login prompt, so it stays on the arrival of
				// a genuinely new connection. A LOGOUT does not open one.
				if (notification.OldState == IConnectionService.ConnectionState.None)
				{
					var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip)
						? ip : "unknown";

					// EventService handles all exception logging, so no try-catch needed here
					await eventService.TriggerEventAsync(SharpEvents.SocketConnect,
						null, // System event (no enactor)
						notification.Handle.ToString(),
						ipAddress);
				}
			}
		}

		// PennMUSH spec: player`disconnect (objid, number of remaining connections, hidden?, cause of disconnection, ip, descriptor, conn() secs, idle() secs, recv bytes/sent bytes/command count)
		//
		// Both endings of a play session raise this: QUIT takes the socket with it, LOGOUT returns it to
		// the connect screen. PennMUSH runs both through disconnect_player and distinguishes them only
		// by the reason it reports, so softcode watching for a player leaving sees a logout too.
		if (notification.OldState == IConnectionService.ConnectionState.LoggedIn &&
				notification.NewState is IConnectionService.ConnectionState.Disconnected
					or IConnectionService.ConnectionState.Connected &&
				notification.PlayerRef.HasValue)
		{
			var connectionData = notification.FormerConnection ?? connectionService.Get(notification.Handle);
			// The portal's background terminal logged in quietly (SocketCommands.CompletePlayerLoginAsync),
			// so it leaves quietly too.
			if (connectionData != null && connectionData.PresenceClass != PresenceClasses.Portal)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip)
					? ip : "unknown";

				// ConnectionService.Disconnect publishes this notification before it removes the
				// disconnecting handle from its session-state dictionary (Unbind does the opposite,
				// and says so in a comment there), so a naive count of the player's connections here
				// would still include the one that is on its way out. Exclude it by handle instead of
				// relying on removal order — correct however the two ever end up sequenced.
				//
				// That handle-exclusion alone isn't enough when the SAME player disconnects on two
				// handles at genuinely the same time: each handler would exclude only its own handle
				// and see the other's as still present, so both would compute "1 remaining" instead of
				// one seeing 1 and the other (whichever actually runs second) seeing 0. Disconnect
				// closes that gap itself, atomically with marking the handle as leaving, and hands the
				// correct count through the notification — prefer it when present (the QUIT path).
				// Unbind's LOGOUT path carries no such count (it doesn't need one — see the notification
				// field's doc comment), so it falls back to the same recomputation as before.
				var remainingConnections = notification.RemainingConnections
					?? await connectionService.Get(notification.PlayerRef.Value)
						.CountAsync(c => c.Handle != notification.Handle && c.PresenceClass != PresenceClasses.Portal);

				var connectedSecs = connectionData.Connected?.TotalSeconds.ToString("F0") ?? "0";
				var idleSecs = connectionData.Idle?.TotalSeconds.ToString("F0") ?? "0";
				var bytesRecv = connectionData.Metadata.TryGetValue("BytesReceived", out var recv) ? recv : "0";
				var bytesSent = connectionData.Metadata.TryGetValue("BytesSent", out var sent) ? sent : "0";
				var commandCount = connectionData.Metadata.TryGetValue("CommandCount", out var count) ? count : "0";

				// A LOGOUT has added its count already, before resetting it for the next login.
				try
				{
					await TotalCommands.AddSessionAsync(mediator, notification.PlayerRef.Value, connectionData.CommandCount);
				}
				catch (Exception ex)
				{
					// The disconnect events and announcements below matter more than the running total.
					logger.LogError(ex, "Error adding the session's commands to TOTCMDS for {Player}", notification.PlayerRef.Value);
				}

				try
				{
					await RecordSessionAsync(notification, connectionData);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error recording the session in CONNINFO for {Player}", notification.PlayerRef.Value);
				}

				await eventService.TriggerEventAsync(SharpEvents.PlayerDisconnect,
					notification.PlayerRef.Value, // Enactor is the disconnecting player
					$"#{notification.PlayerRef.Value.Number}",
					remainingConnections.ToString(),
					connectionData.IsHidden ? "1" : "0", // hidden? (0 = not hidden, 1 = hidden)
					notification.NewState == IConnectionService.ConnectionState.Connected
						? "logout"
						: "quit", // cause of disconnection
					ipAddress,
					notification.Handle.ToString(),
					connectedSecs,
					idleSecs,
					$"{bytesRecv}/{bytesSent}/{commandCount}");

				// PennMUSH tells the room and the player's contents, never the player. A character's other
				// connections are told here instead, so they learn one of theirs has gone; the one leaving
				// got the quit message. The leaving handle may still be listed, so it is excluded by handle.
				if (remainingConnections > 0)
				{
					await foreach (var other in connectionService.Get(notification.PlayerRef.Value)
						.Where(c => c.Handle != notification.Handle && c.PresenceClass != PresenceClasses.Portal))
					{
						await notifyService.Notify(other.Handle, ErrorMessages.Notifications.YouHavePartiallyDisconnected);
					}
				}

				// Resolve the player object once, while it's still in the DB, and share it between
				// the connection-announce call and the room-contents refresh below.
				if (await mediator.Send(new GetObjectNodeQuery(notification.PlayerRef.Value))
						is AnySharpObject and SharpPlayer player)
				{
					await connectionAnnounceService.AnnounceDisconnectAsync(
						player,
						remainingConnections,
						connectionData.IsHidden);

					// Refresh the room's remaining occupants after the player disconnects.
					var roomContainer = await player.Location.WithCancellation(CancellationToken.None);
					var roomDbref = roomContainer.Object().DBRef.ToString();
					await eventService.TriggerEventAsync(SharpEvents.RoomContents,
						notification.PlayerRef.Value,
						roomDbref,
						"disconnect");
				}
			}
		}

		// PennMUSH spec: socket`disconnect (former descriptor, former ip, cause of disconnection, recv bytes/sent bytes/command count)
		if (notification.NewState == IConnectionService.ConnectionState.Disconnected)
		{
			var connectionData = notification.FormerConnection ?? connectionService.Get(notification.Handle);
			if (connectionData != null)
			{
				var ipAddress = connectionData.Metadata.TryGetValue("InternetProtocolAddress", out var ip)
					? ip : "unknown";

				var bytesRecv = connectionData.Metadata.TryGetValue("BytesReceived", out var recv) ? recv : "0";
				var bytesSent = connectionData.Metadata.TryGetValue("BytesSent", out var sent) ? sent : "0";
				var commandCount = connectionData.Metadata.TryGetValue("CommandCount", out var count) ? count : "0";

				// EventService handles all exception logging
				await eventService.TriggerEventAsync(SharpEvents.SocketDisconnect,
					null, // System event (no enactor)
					notification.Handle.ToString(),
					ipAddress,
					"disconnect", // cause of disconnection
					$"{bytesRecv}/{bytesSent}/{commandCount}"); // PennMUSH format: recv/sent/count
			}
		}
	}

	/// <summary>
	/// Counts the ended session in the player's <c>CONNINFO</c>, beside the player's other play sessions still open:
	/// those the disconnect saw as it removed this one, or, for a LOGOUT, those open now. The portal's background
	/// session is not one, here as in the announcements.
	/// </summary>
	private async ValueTask RecordSessionAsync(ConnectionStateChangeNotification notification,
		IConnectionService.ConnectionData ended)
	{
		var player = notification.PlayerRef!.Value;
		var now = DateTimeOffset.UtcNow;
		await ConnectionStatistics.SessionEndedAsync(mediator, player, ended.LoggedInAt ?? now, now,
			async () => notification.OthersLoggedInAt ?? await connectionService.Get(player)
				.Where(c => c.Handle != notification.Handle && c.PresenceClass != PresenceClasses.Portal)
				.Select(c => c.LoggedInAt ?? now)
				.ToListAsync());
	}
}
