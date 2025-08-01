using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

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
	ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);
}