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

	ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	/// <summary>
	/// Begin a batching scope for the specified connection handle.
	/// All Notify calls for this handle will be accumulated until EndBatchingScope is called.
	/// Scopes are ref-counted to support nested @dolists.
	/// </summary>
	void BeginBatchingScope(long handle);

	/// <summary>
	/// End a batching scope for the specified connection handle.
	/// If this is the outermost scope (ref count reaches 0), all accumulated messages are published.
	/// </summary>
	ValueTask EndBatchingScope(long handle);
}