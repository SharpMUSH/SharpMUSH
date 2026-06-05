using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SharpMUSH.Server.Hubs;

/// <summary>
/// SignalR hub for portal real-time communication (scenes, presence, wiki, mail, notifications).
/// Requires JWT authentication via [Authorize] attribute.
/// Groups map to: scene_{sceneId}, presence (global), wiki (global), mail_{accountId}.
/// </summary>
[Authorize]
public class PortalHub : Hub<IPortalHubClient>
{
	private readonly ILogger<PortalHub> _logger;

	public PortalHub(ILogger<PortalHub> logger)
	{
		_logger = logger;
	}

	public override async Task OnConnectedAsync()
	{
		var userId = Context.User?.FindFirst("sub")?.Value ?? "unknown";
		_logger.LogInformation("[SignalR] Portal hub client connected: {ConnectionId} (user: {UserId})", Context.ConnectionId, userId);

		// Add to global presence group so server can broadcast presence updates
		await Groups.AddToGroupAsync(Context.ConnectionId, "presence");

		// Add to global wiki group
		await Groups.AddToGroupAsync(Context.ConnectionId, "wiki");

		// Add to mail group keyed by account (user/account id from JWT)
		if (!string.IsNullOrEmpty(userId))
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, $"mail_{userId}");
		}

		await base.OnConnectedAsync();
	}

	public override async Task OnDisconnectedAsync(Exception? exception)
	{
		var userId = Context.User?.FindFirst("sub")?.Value ?? "unknown";
		_logger.LogInformation("[SignalR] Portal hub client disconnected: {ConnectionId} (user: {UserId})", Context.ConnectionId, userId);
		await base.OnDisconnectedAsync(exception);
	}

	/// <summary>
	/// Adds the client to a scene group so it receives pose/presence updates for that scene.
	/// </summary>
	public async Task JoinScene(string sceneId)
	{
		if (string.IsNullOrWhiteSpace(sceneId))
			throw new ArgumentException("Scene ID cannot be empty.", nameof(sceneId));

		await Groups.AddToGroupAsync(Context.ConnectionId, $"scene_{sceneId}");
		_logger.LogDebug("[SignalR] Client {ConnectionId} joined scene {SceneId}", Context.ConnectionId, sceneId);
	}

	/// <summary>
	/// Removes the client from a scene group.
	/// </summary>
	public async Task LeaveScene(string sceneId)
	{
		if (string.IsNullOrWhiteSpace(sceneId))
			throw new ArgumentException("Scene ID cannot be empty.", nameof(sceneId));

		await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"scene_{sceneId}");
		_logger.LogDebug("[SignalR] Client {ConnectionId} left scene {SceneId}", Context.ConnectionId, sceneId);
	}

	/// <summary>
	/// Explicitly subscribes to presence updates (already in group on connect, but can be called for clarity).
	/// </summary>
	public async Task SubscribePresence()
	{
		await Groups.AddToGroupAsync(Context.ConnectionId, "presence");
		_logger.LogDebug("[SignalR] Client {ConnectionId} subscribed to presence", Context.ConnectionId);
	}

	/// <summary>
	/// Explicitly subscribes to wiki update notifications.
	/// </summary>
	public async Task SubscribeWiki()
	{
		await Groups.AddToGroupAsync(Context.ConnectionId, "wiki");
		_logger.LogDebug("[SignalR] Client {ConnectionId} subscribed to wiki updates", Context.ConnectionId);
	}
}
