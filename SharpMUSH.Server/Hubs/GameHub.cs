using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Messaging.Abstractions;
using System.Security.Claims;

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
///   JoinScene    — adds the client to group "scene:{sceneId}"
///   LeaveScene   — removes the client from group "scene:{sceneId}"
/// </summary>
[Authorize]
public class GameHub(IMessageBus messageBus, ILogger<GameHub> logger) : Hub<IGameHubClient>
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

	/// <summary>
	/// Formats a SignalR group name for a scene.
	/// </summary>
	public static string SceneGroupName(string sceneId) => $"scene:{sceneId}";

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
	/// Publishes a <see cref="GameCommandMessage"/> to NATS (subject
	/// <c>{prefix}.game-command</c>) for the engine to consume.
	/// </summary>
	/// <param name="command">The raw command string typed by the player.</param>
	public async Task SendCommand(string command)
	{
		var dbref = Context.User?.FindFirst(CharacterDbrefClaim)?.Value ?? string.Empty;
		logger.LogDebug("[GameHub] Connection {ConnectionId} (char:{Dbref}) sent command: {Command}",
			Context.ConnectionId, dbref, command);

		var message = new GameCommandMessage(dbref, command, DateTimeOffset.UtcNow);
		await messageBus.Publish(message);
		logger.LogDebug("[GameHub] Published GameCommandMessage for char:{Dbref} at {Timestamp}",
			message.CharacterDbref, message.Timestamp);
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

	/// <summary>
	/// Adds the calling connection to the SignalR group for the specified scene.
	/// The client calls this when opening a live scene view.
	/// </summary>
	/// <param name="sceneId">The id of the scene to subscribe to.</param>
	public async Task JoinScene(string sceneId)
	{
		await Groups.AddToGroupAsync(Context.ConnectionId, SceneGroupName(sceneId));
		logger.LogDebug("[GameHub] Connection {ConnectionId} joined scene group {Group}",
			Context.ConnectionId, SceneGroupName(sceneId));
	}

	/// <summary>
	/// Removes the calling connection from the SignalR group for the specified scene.
	/// The client calls this when leaving a live scene view.
	/// </summary>
	/// <param name="sceneId">The id of the scene to unsubscribe from.</param>
	public async Task LeaveScene(string sceneId)
	{
		await Groups.RemoveFromGroupAsync(Context.ConnectionId, SceneGroupName(sceneId));
		logger.LogDebug("[GameHub] Connection {ConnectionId} left scene group {Group}",
			Context.ConnectionId, SceneGroupName(sceneId));
	}

	// ── Server-side write operations ────────────────────────────────────────
	// These are called by internal services (e.g. NatsBridgeService, REST controllers)
	// to push output to connected clients.  They are NOT exposed as client-invokable
	// hub methods — callers must inject IHubContext<GameHub, IGameHubClient>.

	/// <summary>
	/// Sends a <see cref="GameOutputMessage"/> to all connections belonging to a specific character.
	/// </summary>
	/// <param name="hubContext">The hub context injected by the calling service.</param>
	/// <param name="characterDbref">The character's dbref string (e.g. "#42").</param>
	/// <param name="message">The output message to deliver.</param>
	public static Task SendToCharacterAsync(
		IHubContext<GameHub, IGameHubClient> hubContext,
		string characterDbref,
		GameOutputMessage message) =>
		hubContext.Clients.Group(CharacterGroupName(characterDbref)).ReceiveOutput(message);

	/// <summary>
	/// Broadcasts a <see cref="RoomEventMessage"/> to all connections observing a room.
	/// </summary>
	/// <param name="hubContext">The hub context injected by the calling service.</param>
	/// <param name="roomDbref">The room's dbref string (e.g. "#1").</param>
	/// <param name="message">The room event message to broadcast.</param>
	public static Task SendToRoomAsync(
		IHubContext<GameHub, IGameHubClient> hubContext,
		string roomDbref,
		RoomEventMessage message) =>
		hubContext.Clients.Group(RoomGroupName(roomDbref)).ReceiveRoomEvent(message);

	/// <summary>
	/// Broadcasts a system <see cref="GameOutputMessage"/> to all currently connected clients.
	/// Use sparingly — intended for server-wide announcements (e.g. scheduled maintenance).
	/// </summary>
	/// <param name="hubContext">The hub context injected by the calling service.</param>
	/// <param name="content">The announcement text.</param>
	public static Task BroadcastSystemMessageAsync(
		IHubContext<GameHub, IGameHubClient> hubContext,
		string content) =>
		hubContext.Clients.All.ReceiveOutput(new GameOutputMessage(
			CharacterDbref: "*",
			Content: content,
			Timestamp: DateTimeOffset.UtcNow,
			MessageType: MessageType.System));

	/// <summary>
	/// Returns the DBRef claim value for the connection that is currently executing a hub method.
	/// Convenience wrapper so hub methods do not need to inline the claim lookup.
	/// </summary>
	protected string? CurrentCharacterDbref =>
		Context.User?.FindFirstValue(CharacterDbrefClaim);
}
