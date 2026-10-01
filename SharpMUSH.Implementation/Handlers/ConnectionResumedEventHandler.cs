using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Sends a resumed session the state a connect sends: every package that carries current state is
/// re-sent on connect and on resume, so a client never depends on having seen an earlier push
/// (docs/design/d1/README.md §7.1). The connection server's replay covers only the frames after the
/// client's <c>lastSeq</c>, and a reloaded page holds none of the ones before it.
/// </summary>
/// <remarks>
/// The events are the ones <c>CompletePlayerLoginAsync</c> fires on connect, in the same order, with
/// cause <c>resume</c>: <see cref="SharpEvents.PlayerChannels"/> for the player's channel list, then
/// <see cref="SharpEvents.RoomContents"/> for the room they are in. The bundled handlers send a
/// <c>resume</c> to the resuming player alone; nothing changed for anyone else.
/// </remarks>
public class ConnectionResumedEventHandler(
	IEventService eventService,
	IMUSHCodeParser parser,
	IMediator mediator)
	: INotificationHandler<ConnectionResumedNotification>
{
	public const string Cause = "resume";

	public async ValueTask Handle(ConnectionResumedNotification notification, CancellationToken cancellationToken)
	{
		if (await mediator.Send(new GetObjectNodeQuery(notification.Player), cancellationToken)
				is not (AnySharpObject and SharpPlayer player))
			return;

		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.PlayerChannels,
			notification.Player,
			player.Object.DBRef.ToString(),
			Cause,
			string.Empty);

		var room = await player.Location.WithCancellation(cancellationToken);
		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.RoomContents,
			notification.Player,
			room.Object().DBRef.ToString(),
			Cause);
	}
}
