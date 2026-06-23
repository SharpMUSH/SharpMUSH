using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handles object-related notifications and triggers corresponding PennMUSH-compatible events.
/// </summary>
public class ObjectEventHandlers(
	IEventService eventService,
	IMUSHCodeParser parser)
	: INotificationHandler<ObjectMovedNotification>,
		INotificationHandler<ObjectFlagChangedNotification>
{
	public async ValueTask Handle(ObjectMovedNotification notification, CancellationToken cancellationToken)
	{
		// Trigger OBJECT`MOVE event
		// PennMUSH spec: object`move (objid, newloc, origloc, issilent, cause)
		await eventService.TriggerEventAsync(
			parser,
			"OBJECT`MOVE",
			notification.Enactor,
			notification.Target.Match(
				player => player.Object.DBRef.ToString(),
				exit => exit.Object.DBRef.ToString(),
				thing => thing.Object.DBRef.ToString()),
			notification.NewLocation.Match(
				player => player.Object.DBRef.ToString(),
				room => room.Object.DBRef.ToString(),
				thing => thing.Object.DBRef.ToString()),
			notification.OldLocation.ToString(),
			notification.IsSilent ? "1" : "0",
			notification.Cause);

		// Room-scoped ROOM`CONTENTS so the handler can refresh ALL occupants of the affected
		// rooms (not just the mover): one fire for the destination, one for the origin.
		var newLocDbref = notification.NewLocation.Match(
			player => player.Object.DBRef.ToString(),
			room => room.Object.DBRef.ToString(),
			thing => thing.Object.DBRef.ToString());

		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.RoomContents,
			notification.Enactor,
			newLocDbref,
			"move-in");

		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.RoomContents,
			notification.Enactor,
			notification.OldLocation.ToString(),
			"move-out");
	}

	public async ValueTask Handle(ObjectFlagChangedNotification notification, CancellationToken cancellationToken)
	{
		// Trigger OBJECT`FLAG event  
		// PennMUSH spec: object`flag (objid of object with flag, flag name, type, setbool, setstr)
		await eventService.TriggerEventAsync(
			parser,
			"OBJECT`FLAG",
			notification.Enactor,
			notification.Target.Match(
				player => player.Object.DBRef.ToString(),
				room => room.Object.DBRef.ToString(),
				thing => thing.Object.DBRef.ToString(),
				exit => exit.Object.DBRef.ToString()),
			notification.FlagName,
			notification.Type,
			notification.IsSet ? "1" : "0",
			notification.IsSet ? "SET" : "CLEARED");
	}
}
