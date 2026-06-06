using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Server.Hubs;

/// <summary>
/// Strongly-typed interface for server-to-client SignalR method calls on GameHub.
/// </summary>
public interface IGameHubClient
{
	/// <summary>Sends output text from the game engine to the character's browser.</summary>
	Task ReceiveOutput(GameOutputMessage msg);

	/// <summary>Broadcasts a room event (arrive, depart, say, pose) to room observers.</summary>
	Task ReceiveRoomEvent(RoomEventMessage msg);
}

/// <summary>
/// SignalR hub that provides real-time, bidirectional communication between
/// web portal clients and the SharpMUSH game engine.
///
/// Connection lifecycle:
///   OnConnectedAsync  — adds the client to group "char:{character_dbref}"
///   OnDisconnectedAsync — removes the client from all groups it joined
///
/// Client-to-server:
///   SendCommand  — forwards a player command to the game engine via NATS
///   JoinRoom     — adds the client to group "room:{roomDbref}"
///   LeaveRoom    — removes the client from group "room:{roomDbref}"
/// </summary>
[Authorize]
public class GameHub(ILogger<GameHub> logger) : Hub<IGameHubClient>
{
	/// <summary>Claim name that carries the authenticated character's dbref.</summary>
	public const string CharacterDbrefClaim = "character_dbref";

	/// <summary>
	/// Formats a SignalR group name for a character.
	/// </summary>
	public static string CharacterGroupName(string dbref) => $"char:{dbref}";

	/// <summary>
	/// Formats a SignalR group name for a room.
	/// </summary>
	public static string RoomGroupName(string dbref) => $"room:{dbref}";

	/// <inheritdoc/>
	public override async Task OnConnectedAsync()
	{
		var dbref = Context.User?.FindFirst(CharacterDbrefClaim)?.Value;
		if (!string.IsNullOrWhiteSpace(dbref))
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, CharacterGroupName(dbref));
			logger.LogInformation("[GameHub] Connection {ConnectionId} joined character group {Group}",
				Context.ConnectionId, CharacterGroupName(dbref));
		}
		else
		{
			logger.LogWarning("[GameHub] Connection {ConnectionId} has no {Claim} claim; not added to character group",
				Context.ConnectionId, CharacterDbrefClaim);
		}

		await base.OnConnectedAsync();
	}

	/// <inheritdoc/>
	public override async Task OnDisconnectedAsync(Exception? exception)
	{
		logger.LogInformation("[GameHub] Connection {ConnectionId} disconnected", Context.ConnectionId);
		await base.OnDisconnectedAsync(exception);
	}

	/// <summary>
	/// Client invokes this to send a command string to the game engine.
	/// Currently creates a <see cref="GameCommandMessage"/> for downstream processing.
	/// </summary>
	/// <param name="command">The raw command string typed by the player.</param>
	public Task SendCommand(string command)
	{
		var dbref = Context.User?.FindFirst(CharacterDbrefClaim)?.Value ?? string.Empty;
		logger.LogDebug("[GameHub] Connection {ConnectionId} (char:{Dbref}) sent command: {Command}",
			Context.ConnectionId, dbref, command);

		// The game command message is created here and will be consumed downstream.
		// Future task: publish to NATS so the engine picks it up.
		var message = new GameCommandMessage(dbref, command, DateTimeOffset.UtcNow);
		logger.LogDebug("[GameHub] Created GameCommandMessage for char:{Dbref} at {Timestamp}",
			message.CharacterDbref, message.Timestamp);

		return Task.CompletedTask;
	}

	/// <summary>
	/// Adds the calling connection to the SignalR group for the specified room.
	/// The client calls this after moving to a new room.
	/// </summary>
	/// <param name="roomDbref">The dbref of the room to subscribe to.</param>
	public async Task JoinRoom(string roomDbref)
	{
		await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroupName(roomDbref));
		logger.LogDebug("[GameHub] Connection {ConnectionId} joined room group {Group}",
			Context.ConnectionId, RoomGroupName(roomDbref));
	}

	/// <summary>
	/// Removes the calling connection from the SignalR group for the specified room.
	/// The client calls this before moving away from a room.
	/// </summary>
	/// <param name="roomDbref">The dbref of the room to unsubscribe from.</param>
	public async Task LeaveRoom(string roomDbref)
	{
		await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroupName(roomDbref));
		logger.LogDebug("[GameHub] Connection {ConnectionId} left room group {Group}",
			Context.ConnectionId, RoomGroupName(roomDbref));
	}
}
