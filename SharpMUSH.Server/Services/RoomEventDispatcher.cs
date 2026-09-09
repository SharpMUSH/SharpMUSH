using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Reality;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Services;

public interface IRoomEventDispatcher
{
	Task DispatchAsync(RoomEventMessage message, CancellationToken ct = default);
}

public sealed class RoomEventDispatcher(IHubContext<GameHub, IGameHubClient> hub,
	HubConnectionRegistry registry, IVisibleWorldProjection projection, IRealityPolicy reality, ILogger<RoomEventDispatcher> logger) : IRoomEventDispatcher
{
	public async Task DispatchAsync(RoomEventMessage message, CancellationToken ct = default)
	{
		if (!DBRef.TryParse(message.RoomDbref, out var room) || room is not { IsObjid: true }) return;
		var enabled = await reality.IsEnabledAsync(ct);
		var hasSource = DBRef.TryParse(message.ActorDbref, out var source) && source is { IsObjid: true };
		if (enabled && !hasSource) return;
		foreach (var subscription in registry.Subscribers(room.Value))
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				if (!registry.IsCurrent(subscription)
					|| !(hasSource
						? await projection.CanReceiveRoomEventAsync(subscription.Actor, room.Value, source!.Value, message.EventType, ct)
						: await projection.CanSubscribeRoomAsync(subscription.Actor, room.Value, ct))
					|| !registry.IsCurrent(subscription)) continue;
				await hub.Clients.Client(subscription.ConnectionId).ReceiveRoomEvent(message);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogWarning(ex, "Room event delivery failed for connection {ConnectionId}", subscription.ConnectionId);
			}
		}
	}
}
