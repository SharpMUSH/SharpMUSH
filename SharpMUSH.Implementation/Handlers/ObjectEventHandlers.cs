using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
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
			notification.Target.Value switch { SharpPlayer p => p.Object.DBRef.ToString(), SharpExit e => e.Object.DBRef.ToString(), SharpThing t => t.Object.DBRef.ToString(), _ => throw new InvalidOperationException() },
			notification.NewLocation.Value switch { SharpPlayer p => p.Object.DBRef.ToString(), SharpRoom r => r.Object.DBRef.ToString(), SharpThing t => t.Object.DBRef.ToString(), _ => throw new InvalidOperationException() },
			notification.OldLocation.ToString(),
			notification.IsSilent ? "1" : "0",
			notification.Cause);
	}

	public async ValueTask Handle(ObjectFlagChangedNotification notification, CancellationToken cancellationToken)
	{
		// Trigger OBJECT`FLAG event  
		// PennMUSH spec: object`flag (objid of object with flag, flag name, type, setbool, setstr)
		await eventService.TriggerEventAsync(
			parser,
			"OBJECT`FLAG",
			notification.Enactor,
			notification.Target.Value switch { SharpPlayer p => p.Object.DBRef.ToString(), SharpRoom r => r.Object.DBRef.ToString(), SharpExit e => e.Object.DBRef.ToString(), SharpThing t => t.Object.DBRef.ToString(), _ => throw new InvalidOperationException() },
			notification.FlagName,
			notification.Type,
			notification.IsSet ? "1" : "0",
			notification.IsSet ? "SET" : "CLEARED");
	}
}
