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
	HubConnectionRegistry registry, IVisibleWorldProjection projection, IRealityPolicy reality) : IRoomEventDispatcher
{
	public async Task DispatchAsync(RoomEventMessage message, CancellationToken ct = default)
	{
		if (!DBRef.TryParse(message.RoomDbref, out var room) || room is not { IsObjid: true }) return;
		if (!await reality.IsEnabledAsync(ct))
		{
			await hub.Clients.Group(GameHub.RoomGroupName(room.Value)).ReceiveRoomEvent(message);
			return;
		}
		if (!DBRef.TryParse(message.ActorDbref, out var source) || source is not { IsObjid: true }) return;
		foreach (var subscription in registry.Subscribers(room.Value))
		{
			ct.ThrowIfCancellationRequested();
			if (!registry.IsCurrent(subscription)
				|| !await projection.CanReceiveRoomEventAsync(subscription.Actor, room.Value, source.Value, message.EventType, ct)
				|| !registry.IsCurrent(subscription)) continue;
			await hub.Clients.Client(subscription.ConnectionId).ReceiveRoomEvent(message);
		}
	}
}
