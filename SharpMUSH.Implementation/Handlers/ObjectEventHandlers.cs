using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handles object-related notifications and triggers corresponding PennMUSH-compatible events.
/// </summary>
public class ObjectEventHandlers(
	IEventService eventService)
	: INotificationHandler<ObjectMovedNotification>,
		INotificationHandler<ObjectFlagChangedNotification>
{
	public async ValueTask Handle(ObjectMovedNotification notification, CancellationToken cancellationToken)
	{
		// PennMUSH spec: object`move (objid, newloc, origloc, issilent, cause)
		await eventService.TriggerEventAsync(SharpEvents.ObjectMove,
			notification.Enactor,
			notification.Target.Object().DBRef.ToString(),
			notification.NewLocation.Object().DBRef.ToString(),
			notification.OldLocation.ToString(),
			notification.IsSilent ? "1" : "0",
			notification.Cause);

		// Room-scoped ROOM`CONTENTS so the handler can refresh ALL occupants of the affected
		// rooms (not just the mover): one fire for the destination, one for the origin. The enactor is
		// whoever caused the move (the wizard who @tel'd someone), so the moved object goes along as mover.
		var newLocDbref = notification.NewLocation.Object().DBRef.ToString();
		var mover = notification.Target.Object().DBRef.ToString();

		await eventService.TriggerEventAsync(SharpEvents.RoomContents,
			notification.Enactor,
			newLocDbref,
			"move-in",
			mover);

		await eventService.TriggerEventAsync(SharpEvents.RoomContents,
			notification.Enactor,
			notification.OldLocation.ToString(),
			"move-out",
			mover);
	}

	public async ValueTask Handle(ObjectFlagChangedNotification notification, CancellationToken cancellationToken)
	{
		// PennMUSH spec: object`flag (objid of object with flag, flag name, type, setbool, setstr)
		await eventService.TriggerEventAsync(SharpEvents.ObjectFlag,
			notification.Enactor,
			notification.Target.Object().DBRef.ToString(),
			notification.FlagName,
			notification.Type,
			notification.IsSet ? "1" : "0",
			notification.IsSet ? "SET" : "CLEARED");
	}
}
