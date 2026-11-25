using System.Text.Json.Nodes;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Tells a player's portal connections who they play and where: the engine's <c>sharp.play</c> package,
/// <c>{"v":1,"character":"#12:…","name":"…","room":"#0:…"}</c>. It goes on the play connection itself, so the portal
/// reads it in order with every other package, and to guests as to anyone.
/// </summary>
/// <remarks>
/// Sent to the connection that logs in, to one that resumes (its page may have reloaded and hold nothing), and to
/// every portal connection of a player who moves. Telnet connections are not sent it.
/// </remarks>
public class PlayStateOobHandler(IConnectionService connectionService, IMediator mediator, IMessageBus messageBus)
	: INotificationHandler<ConnectionStateChangeNotification>,
		INotificationHandler<ConnectionResumedNotification>,
		INotificationHandler<ObjectMovedNotification>
{
	public const string WebSocketConnection = "websocket";

	public async ValueTask Handle(ConnectionStateChangeNotification notification, CancellationToken cancellationToken)
	{
		if (notification.NewState != IConnectionService.ConnectionState.LoggedIn
				|| notification.PlayerRef is not { } playerRef)
			return;

		await SendToHandleAsync(notification.Handle, playerRef, cancellationToken);
	}

	public async ValueTask Handle(ConnectionResumedNotification notification, CancellationToken cancellationToken) =>
		await SendToHandleAsync(notification.Handle, notification.Player, cancellationToken);

	public async ValueTask Handle(ObjectMovedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Target is not SharpPlayer player) return;

		var frame = Frame(player, notification.NewLocation.Object().DBRef);
		await foreach (var connection in connectionService.Get(player.Object.DBRef).WithCancellation(cancellationToken))
		{
			if (connection.ConnectionType == WebSocketConnection)
				await messageBus.Publish(new WebSocketOutputMessage(connection.Handle, frame));
		}
	}

	private async ValueTask SendToHandleAsync(long handle, DBRef playerRef, CancellationToken cancellationToken)
	{
		if (connectionService.Get(handle) is not { ConnectionType: WebSocketConnection }) return;
		if (await mediator.Send(new GetObjectNodeQuery(playerRef), cancellationToken) is not (AnySharpObject and SharpPlayer player))
			return;

		var room = await player.Location.WithCancellation(cancellationToken);
		await messageBus.Publish(new WebSocketOutputMessage(handle, Frame(player, room.Object().DBRef)));
	}

	/// <summary>The <c>sharp.play</c> envelope for <paramref name="player"/> in <paramref name="room"/>.</summary>
	public static string Frame(SharpPlayer player, DBRef room) =>
		WebSocketOobEnvelope.Build(WebSocketOobEnvelope.PlayPackage, new JsonObject
		{
			["v"] = 1,
			["character"] = player.Object.DBRef.ToString(),
			["name"] = player.Object.Name,
			["room"] = room.ToString()
		}.ToJsonString());
}
