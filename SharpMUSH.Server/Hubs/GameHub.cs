using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server.Authentication;
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

	/// <summary>
	/// Generic signal that the server's loaded-plugin set changed (a plugin DLL was unloaded or reloaded).
	/// Carries no plugin-specific payload by design: the client reacts by forcing a hard browser refresh,
	/// the only way to reclaim a compiled component assembly the WASM runtime may have loaded.
	/// </summary>
	Task ReceivePluginsChanged();
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
///
/// (Scene realtime — JoinScene/LeaveScene/ReceiveSceneMessage — moved out of this hub into the Scene
/// plugin's own SceneHub at /hubs/scene; see SharpMUSH.Plugins.Scene/Web. This hub is scene-agnostic.)
/// </summary>
[Authorize]
public class GameHub(IMessageBus messageBus, ILogger<GameHub> logger, HubConnectionRegistry registry, SitelockGuard sitelockGuard) : Hub<IGameHubClient>
{
	/// <summary>Claim name that carries the authenticated character's dbref.</summary>
	public const string CharacterDbrefClaim = "character_dbref";

	/// <summary>
	/// The SignalR group for a character. Takes a <see cref="DBRef"/> rather than a string so
	/// subscriber and publisher cannot disagree on a spelling: <see cref="DBRef.ToString"/> is the
	/// one serialization, and <see cref="DBRef.TryParse"/> the one way in from the wire.
	/// </summary>
	public static string CharacterGroupName(DBRef character) => $"char:{character}";

	/// <summary>
	/// The SignalR group for a room. Takes a <see cref="DBRef"/> for the same reason as
	/// <see cref="CharacterGroupName"/>.
	/// </summary>
	public static string RoomGroupName(DBRef room) => $"room:{room}";

	/// <inheritdoc/>
	public override async Task OnConnectedAsync()
	{
		var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		if (sitelockGuard.IsBlocked(ip, host: "", SitelockGuard.Connect))
		{
			logger.LogWarning("[GameHub] Connection {ConnectionId} (ip {Ip}) blocked by sitelock rule; aborting",
				Context.ConnectionId, ip);
			Context.Abort();
			return;
		}

		if (Context.User?.GetActingCharacter() is { IsObjid: true } character)
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, CharacterGroupName(character));
			logger.LogInformation("[GameHub] Connection {ConnectionId} joined character group {Group}",
				Context.ConnectionId, CharacterGroupName(character));
		}
		else
		{
			// A bare dbref is refused rather than routed: it would name char:#N while publishers
			// name char:#N:creation, and the connection would receive nothing with no error anywhere.
			logger.LogWarning(
				"[GameHub] Connection {ConnectionId} has no usable {Claim} claim (absent, unparseable, " +
				"or a bare dbref rather than an objid); not added to character group",
				Context.ConnectionId, CharacterDbrefClaim);
		}

		var accountId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (accountId is not null)
			registry.Add(Context.ConnectionId, accountId, ip, () => Context.Abort());

		await base.OnConnectedAsync();
	}

	/// <inheritdoc/>
	public override async Task OnDisconnectedAsync(Exception? exception)
	{
		logger.LogInformation("[GameHub] Connection {ConnectionId} disconnected", Context.ConnectionId);
		registry.Remove(Context.ConnectionId);
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
		if (Context.User?.GetActingCharacter() is not { IsObjid: true } character)
		{
			// No routable character identity → the command is unroutable; fail at the auth boundary
			// rather than publishing a reference the engine cannot reply to onto the bus.
			logger.LogWarning(
				"[GameHub] Connection {ConnectionId} sent a command without a usable {Claim} claim " +
				"(absent, unparseable, or a bare dbref rather than an objid); rejecting",
				Context.ConnectionId, CharacterDbrefClaim);
			throw new HubException("No character identity on this connection.");
		}

		var dbref = character.ToString();
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
	/// <param name="roomDbref">The room's objid, e.g. <c>"#7:1700000000"</c>.</param>
	/// <exception cref="HubException">The argument is absent or not a parseable dbref or objid.</exception>
	public async Task JoinRoom(string? roomDbref)
	{
		var room = ParseRoomOrThrow(roomDbref);
		await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroupName(room));
		logger.LogDebug("[GameHub] Connection {ConnectionId} joined room group {Group}",
			Context.ConnectionId, RoomGroupName(room));
	}

	/// <summary>
	/// Removes the calling connection from the SignalR group for the specified room.
	/// The client calls this before moving away from a room.
	/// </summary>
	/// <param name="roomDbref">The room's objid, e.g. <c>"#7:1700000000"</c>.</param>
	/// <exception cref="HubException">The argument is absent or not a parseable dbref or objid.</exception>
	public async Task LeaveRoom(string? roomDbref)
	{
		var room = ParseRoomOrThrow(roomDbref);
		await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroupName(room));
		logger.LogDebug("[GameHub] Connection {ConnectionId} left room group {Group}",
			Context.ConnectionId, RoomGroupName(room));
	}

	/// <summary>
	/// Parses a client-supplied room reference. Client input is the one place a malformed
	/// spelling can enter, so it is rejected here rather than silently naming a group nothing
	/// publishes to. The parameter is nullable because a client can send JSON <c>null</c>
	/// regardless of the declared type — nullable reference types are not enforced at runtime.
	/// </summary>
	private DBRef ParseRoomOrThrow(string? roomDbref)
	{
		if (DBRef.TryParse(roomDbref, out var room) && room is { IsObjid: true })
		{
			return room.Value;
		}

		logger.LogWarning("[GameHub] Connection {ConnectionId} sent an unroutable room reference {Room}",
			Context.ConnectionId, roomDbref);
		throw new HubException($"'{roomDbref}' is not a valid objid; a bare dbref cannot be routed.");
	}

	// These are called by internal services (e.g. NatsBridgeService, REST controllers)
	// to push output to connected clients.  They are NOT exposed as client-invokable
	// hub methods — callers must inject IHubContext<GameHub, IGameHubClient>.

	/// <summary>
	/// Sends a <see cref="GameOutputMessage"/> to all connections belonging to a specific character.
	/// </summary>
	/// <param name="hubContext">The hub context injected by the calling service.</param>
	/// <param name="character">The character's objid.</param>
	/// <param name="message">The output message to deliver.</param>
	public static Task SendToCharacterAsync(
		IHubContext<GameHub, IGameHubClient> hubContext,
		DBRef character,
		GameOutputMessage message) =>
		hubContext.Clients.Group(CharacterGroupName(character)).ReceiveOutput(message);

	/// <summary>
	/// Broadcasts a <see cref="RoomEventMessage"/> to all connections observing a room.
	/// </summary>
	/// <param name="hubContext">The hub context injected by the calling service.</param>
	/// <param name="room">The room's objid.</param>
	/// <param name="message">The room event message to broadcast.</param>
	public static Task SendToRoomAsync(
		IHubContext<GameHub, IGameHubClient> hubContext,
		DBRef room,
		RoomEventMessage message) =>
		hubContext.Clients.Group(RoomGroupName(room)).ReceiveRoomEvent(message);

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
			CharacterDbref: null,
			Content: content,
			Timestamp: DateTimeOffset.UtcNow,
			MessageType: MessageType.System));

	/// <summary>
	/// Broadcasts the generic "plugins changed" signal to every connected client. Called by the server's
	/// <see cref="SharpMUSH.Library.Services.Interfaces.IPluginChangeNotifier"/> implementation after a plugin
	/// unload/reload; the client forces a hard refresh on receipt.
	/// </summary>
	/// <param name="hubContext">The hub context injected by the calling service.</param>
	public static Task BroadcastPluginsChangedAsync(
		IHubContext<GameHub, IGameHubClient> hubContext) =>
		hubContext.Clients.All.ReceivePluginsChanged();

	/// <summary>
	/// Returns the DBRef claim value for the connection that is currently executing a hub method.
	/// Convenience wrapper so hub methods do not need to inline the claim lookup.
	/// </summary>
	protected string? CurrentCharacterDbref =>
		Context.User?.FindFirstValue(CharacterDbrefClaim);
}
