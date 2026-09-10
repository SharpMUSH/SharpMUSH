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
		var roomRef = room.Value;
		var enabled = await reality.IsEnabledAsync(ct);
		var hasSource = DBRef.TryParse(message.ActorDbref, out var source) && source is { IsObjid: true };
		if (enabled && !hasSource)
		{
			logger.LogWarning("Dropping room event for {Room}: enabled reality requires a full actor objid", roomRef);
			return;
		}
		await Parallel.ForEachAsync(registry.Subscribers(roomRef),
			new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (subscription, token) =>
		{
			try
			{
				if (!registry.IsCurrent(subscription)
					|| !(hasSource
						? await projection.CanReceiveRoomEventAsync(subscription.Actor, roomRef, source!.Value, message.EventType, token)
						: await projection.CanSubscribeRoomAsync(subscription.Actor, roomRef, token))
					|| !registry.IsCurrent(subscription)) return;
				await hub.Clients.Client(subscription.ConnectionId).ReceiveRoomEvent(message).WaitAsync(token);
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
			{
				logger.LogWarning(ex, "Room event delivery failed for connection {ConnectionId}", subscription.ConnectionId);
			}
		});
	}
}
