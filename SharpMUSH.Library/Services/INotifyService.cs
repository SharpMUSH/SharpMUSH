using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface INotifyService
{
	enum NotificationType
	{
		Emit,
		Say,
		Pose,
		SemiPose,
		Announce,
		NSEmit,
		NSSay,
		NSPose,
		NSSemiPose,
		NSAnnounce
	}

	// TODO: Add a 'sender' for Noisy etc rules.
	Task Notify(DBRef who, MString what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	Task Notify(AnySharpObject who, MString what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	Task Notify(string handle, MString what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	Task Notify(string[] handles, MString what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	Task Notify(DBRef who, string what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	Task Notify(AnySharpObject who, string what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	Task Notify(string handle, string what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	Task Notify(string[] handles, string what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);
}